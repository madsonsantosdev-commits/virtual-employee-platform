using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Customers;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Options;
using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Application.Appointments.CreateAppointment;
using VirtualEmployee.Infrastructure.Appointments;
using VirtualEmployee.Application.Appointments.CancelAppointment;
using VirtualEmployee.Application.Appointments.RescheduleAppointment;
using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Application.Payments.CreatePaymentAttempt;
using VirtualEmployee.Application.Payments.Gateway;
using VirtualEmployee.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VirtualEmployee.IntegrationTests.Payments.Helpers;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Scheduling;

[Collection(PostgreSqlCollection.Name)]
public sealed class AppointmentPersistenceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public AppointmentPersistenceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    #region Facts

    [Fact]
    public async Task PaymentCreation_WithCompletionFailure_ShouldRollbackAndRecoverSameCharge()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var providerPaymentId = Guid.NewGuid().ToString("N");
        var now = CreatedAt.AddMinutes(2);

        var command = new CreatePaymentAttemptCommand(
            appointmentId,
            PaymentMethod.Pix,
            Guid.NewGuid().ToString("N"));

        CreateGatewayPaymentRequest? originalGatewayRequest = null;

        var gateway = new RecordingPaymentGateway(
            (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                originalGatewayRequest ??= request;

                Assert.Equal(originalGatewayRequest, request);

                return Task.FromResult(
                    CreateGatewayPaymentResult.Created(
                        new GatewayPaymentReference(
                            "gateway-test",
                            providerPaymentId,
                            "https://checkout.example.test/recovered-commit",
                            request.ExpiresAt)));
            });

        try
        {
            await SeedAsync(data);

            await using (var seedContext = CreateContext(data.TenantId))
            {
                seedContext.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await seedContext.SaveChangesAsync();
            }

            var interceptor = new FailAfterPaymentCompletionSaveInterceptor();

            await using (var failingContext = CreateContext(
                data.TenantId,
                interceptor))
            {
                var service = new CreatePaymentAttemptService(
                    failingContext,
                    gateway,
                    new FixedTimeProvider(now));

                var exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => service.CreateAsync(command));

                Assert.Equal(
                    "Simulated failure before payment completion commit.",
                    exception.Message);

                Assert.Equal(2, interceptor.SavedEntriesCount);
                Assert.True(interceptor.HadActiveTransaction);
                Assert.Null(failingContext.Database.CurrentTransaction);

                Assert.Empty(
                    failingContext.ChangeTracker.Entries<PaymentAttempt>());
            }

            Assert.Single(gateway.Requests);
            Assert.NotNull(originalGatewayRequest);

            await using (var readContext = CreateContext(data.TenantId))
            {
                var attempt = Assert.Single(
                    await readContext.PaymentAttempts
                        .AsNoTracking()
                        .ToListAsync());

                Assert.Equal(
                    originalGatewayRequest.PaymentAttemptId,
                    attempt.Id);

                Assert.Equal(PaymentAttemptStatus.Created, attempt.Status);
                Assert.Null(attempt.Provider);
                Assert.Null(attempt.ProviderPaymentId);
                Assert.Equal(now, attempt.UpdatedAt);

                var intent = Assert.Single(
                    await readContext.IdempotencyRecords
                        .AsNoTracking()
                        .ToListAsync());

                Assert.Equal(
                    PaymentAttemptPreparationService.IntentOperation,
                    intent.Operation);

                Assert.Equal(1, await readContext.Payments.CountAsync());

                Assert.False(
                    await readContext.IdempotencyRecords.AnyAsync(
                        entity =>
                            entity.Operation ==
                                PaymentAttemptCompletionService.Operation));
            }

            CreatePaymentAttemptResponse recoveredResponse;

            await using (var retryContext = CreateContext(data.TenantId))
            {
                var service = new CreatePaymentAttemptService(
                    retryContext,
                    gateway,
                    new FixedTimeProvider(now.AddMinutes(1)));

                var retry = await service.CreateAsync(command);

                Assert.True(retry.IsSuccess);

                recoveredResponse =
                    Assert.IsType<CreatePaymentAttemptResponse>(
                        retry.Response);

                Assert.Equal(
                    originalGatewayRequest.PaymentId,
                    recoveredResponse.PaymentId);

                Assert.Equal(
                    originalGatewayRequest.PaymentAttemptId,
                    recoveredResponse.PaymentAttemptId);

                var attempt = Assert.Single(
                    await retryContext.PaymentAttempts
                        .AsNoTracking()
                        .ToListAsync());

                Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
                Assert.Equal(providerPaymentId, attempt.ProviderPaymentId);
                Assert.Equal(now, attempt.CreatedAt);
                Assert.Equal(now.AddMinutes(1), attempt.UpdatedAt);

                Assert.Equal(1, await retryContext.Payments.CountAsync());
                Assert.Equal(2, await retryContext.IdempotencyRecords.CountAsync());
            }

            Assert.Equal(2, gateway.Requests.Count);

            Assert.Equal(
                gateway.Requests[0].IdempotencyKey,
                gateway.Requests[1].IdempotencyKey);

            await using (var replayContext = CreateContext(data.TenantId))
            {
                var service = new CreatePaymentAttemptService(
                    replayContext,
                    gateway,
                    new FixedTimeProvider(now.AddMinutes(2)));

                var replay = await service.CreateAsync(command);

                Assert.True(replay.IsSuccess);
                Assert.Equal(recoveredResponse, replay.Response);
            }

            Assert.Equal(2, gateway.Requests.Count);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task PaymentCreation_WithDefinitiveRejection_ShouldReplayFailureAndAllowNewKey()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var now = CreatedAt.AddMinutes(2);
        var gatewayCalls = 0;

        var rejectedCommand = new CreatePaymentAttemptCommand(
            appointmentId,
            PaymentMethod.Pix,
            Guid.NewGuid().ToString("N"));

        var nextCommand = new CreatePaymentAttemptCommand(
            appointmentId,
            PaymentMethod.CreditCard,
            Guid.NewGuid().ToString("N"));

        var gateway = new RecordingPaymentGateway(
            (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var call = Interlocked.Increment(ref gatewayCalls);

                if (call == 1)
                {
                    return Task.FromResult(
                        CreateGatewayPaymentResult.Rejected());
                }

                return Task.FromResult(
                    CreateGatewayPaymentResult.Created(
                        new GatewayPaymentReference(
                            "gateway-test",
                            Guid.NewGuid().ToString("N"),
                            "https://checkout.example.test/new-attempt",
                            request.ExpiresAt)));
            });

        try
        {
            await SeedAsync(data);

            await using (var seedContext = CreateContext(data.TenantId))
            {
                seedContext.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await seedContext.SaveChangesAsync();
            }

            Guid paymentId;
            Guid rejectedAttemptId;

            await using (var firstContext = CreateContext(data.TenantId))
            {
                var service = new CreatePaymentAttemptService(
                    firstContext,
                    gateway,
                    new FixedTimeProvider(now));

                var result = await service.CreateAsync(rejectedCommand);

                Assert.False(result.IsSuccess);
                Assert.Equal(
                    CreatePaymentAttemptError.ProviderError,
                    result.Error);
                Assert.Null(result.Response);

                var payment = Assert.Single(
                    await firstContext.Payments
                        .AsNoTracking()
                        .ToListAsync());

                var attempt = Assert.Single(
                    await firstContext.PaymentAttempts
                        .AsNoTracking()
                        .ToListAsync());

                paymentId = payment.Id;
                rejectedAttemptId = attempt.Id;

                Assert.Equal(PaymentStatus.Pending, payment.Status);
                Assert.Equal(PaymentAttemptStatus.Failed, attempt.Status);
                Assert.Null(attempt.Provider);
                Assert.Null(attempt.ProviderPaymentId);
                Assert.Equal(2, await firstContext.IdempotencyRecords.CountAsync());
            }

            await using (var replayContext = CreateContext(data.TenantId))
            {
                var service = new CreatePaymentAttemptService(
                    replayContext,
                    gateway,
                    new FixedTimeProvider(now.AddMinutes(1)));

                var replay = await service.CreateAsync(rejectedCommand);

                Assert.False(replay.IsSuccess);
                Assert.Equal(
                    CreatePaymentAttemptError.ProviderError,
                    replay.Error);
                Assert.Null(replay.Response);
            }

            Assert.Equal(1, gatewayCalls);
            Assert.Single(gateway.Requests);

            await using (var nextContext = CreateContext(data.TenantId))
            {
                var service = new CreatePaymentAttemptService(
                    nextContext,
                    gateway,
                    new FixedTimeProvider(now.AddMinutes(2)));

                var result = await service.CreateAsync(nextCommand);

                Assert.True(result.IsSuccess);

                var response =
                    Assert.IsType<CreatePaymentAttemptResponse>(
                        result.Response);

                Assert.Equal(paymentId, response.PaymentId);
                Assert.NotEqual(
                    rejectedAttemptId,
                    response.PaymentAttemptId);
                Assert.Equal("CREDIT_CARD", response.PaymentMethod);
                Assert.Equal("PENDING", response.Status);

                var attempts = await nextContext.PaymentAttempts
                    .AsNoTracking()
                    .ToListAsync();

                Assert.Equal(2, attempts.Count);

                var rejectedAttempt = Assert.Single(
                    attempts,
                    attempt => attempt.Id == rejectedAttemptId);

                var nextAttempt = Assert.Single(
                    attempts,
                    attempt => attempt.Id == response.PaymentAttemptId);

                Assert.Equal(
                    PaymentAttemptStatus.Failed,
                    rejectedAttempt.Status);

                Assert.Equal(now, rejectedAttempt.UpdatedAt);
                Assert.Null(rejectedAttempt.ProviderPaymentId);

                Assert.Equal(
                    PaymentAttemptStatus.Pending,
                    nextAttempt.Status);

                Assert.Equal(
                    PaymentMethod.CreditCard,
                    nextAttempt.PaymentMethod);

                Assert.Equal(paymentId, nextAttempt.PaymentId);
                Assert.Equal(rejectedAttempt.Amount, nextAttempt.Amount);

                Assert.Equal(1, await nextContext.Payments.CountAsync());
                Assert.Equal(4, await nextContext.IdempotencyRecords.CountAsync());
            }

            Assert.Equal(2, gatewayCalls);

            var requests = gateway.Requests;

            Assert.Equal(
                requests[0].PaymentId,
                requests[1].PaymentId);

            Assert.NotEqual(
                requests[0].PaymentAttemptId,
                requests[1].PaymentAttemptId);

            Assert.NotEqual(
                requests[0].IdempotencyKey,
                requests[1].IdempotencyKey);

            // A nova tentativa não altera o resultado da chave anterior.
            await using (var finalContext = CreateContext(data.TenantId))
            {
                var service = new CreatePaymentAttemptService(
                    finalContext,
                    gateway,
                    new FixedTimeProvider(now.AddMinutes(3)));

                var replay = await service.CreateAsync(rejectedCommand);

                Assert.Equal(
                    CreatePaymentAttemptError.ProviderError,
                    replay.Error);
                Assert.False(replay.IsSuccess);
            }

            Assert.Equal(2, gatewayCalls);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task PaymentCreation_ShouldCallGatewayOutsideTransactionAndReplayResult()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var providerPaymentId = Guid.NewGuid().ToString("N");
        var now = CreatedAt.AddMinutes(2);

        var command = new CreatePaymentAttemptCommand(
            appointmentId,
            PaymentMethod.Pix,
            Guid.NewGuid().ToString("N"));

        try
        {
            await SeedAsync(data);

            await using (var seedContext = CreateContext(data.TenantId))
            {
                seedContext.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await seedContext.SaveChangesAsync();
            }

            await using var context = CreateContext(data.TenantId);

            var gateway = new RecordingPaymentGateway(
                async (request, cancellationToken) =>
                {
                    Assert.Null(context.Database.CurrentTransaction);
                    Assert.Null(System.Transactions.Transaction.Current);

                    // Outro contexto verifica que a preparação
                    // já foi confirmada antes da chamada ao gateway.
                    await using var observer = CreateContext(data.TenantId);

                    var attempt = await observer.PaymentAttempts
                        .AsNoTracking()
                        .SingleAsync(
                            entity => entity.Id == request.PaymentAttemptId,
                            cancellationToken);

                    Assert.Equal(
                        PaymentAttemptStatus.Created,
                        attempt.Status);

                    Assert.True(
                        await observer.IdempotencyRecords.AnyAsync(
                            entity =>
                                entity.Operation ==
                                    PaymentAttemptPreparationService.IntentOperation &&
                                entity.Key == command.IdempotencyKey,
                            cancellationToken));

                    Assert.Equal(data.TenantId, request.TenantId);
                    Assert.Equal(data.BusinessId, request.BusinessId);
                    Assert.Equal(appointmentId, request.AppointmentId);
                    Assert.Equal(attempt.Amount, request.Amount);

                    return CreateGatewayPaymentResult.Created(
                        new GatewayPaymentReference(
                            "gateway-test",
                            providerPaymentId,
                            "https://checkout.example.test/payment",
                            request.ExpiresAt));
                });

            var service = new CreatePaymentAttemptService(
                context,
                gateway,
                new FixedTimeProvider(now));

            var result = await service.CreateAsync(command);

            Assert.True(result.IsSuccess);

            var response =
                Assert.IsType<CreatePaymentAttemptResponse>(
                    result.Response);

            Assert.Equal("PIX", response.PaymentMethod);
            Assert.Equal("PENDING", response.Status);
            Assert.Equal(65m, response.Amount);
            Assert.Equal(
                "https://checkout.example.test/payment",
                response.CheckoutUrl);

            Assert.Null(context.Database.CurrentTransaction);

            await using var replayContext = CreateContext(data.TenantId);

            var replayService = new CreatePaymentAttemptService(
                replayContext,
                gateway,
                new FixedTimeProvider(now.AddMinutes(1)));

            var replay = await replayService.CreateAsync(command);

            Assert.True(replay.IsSuccess);
            Assert.Equal(response, replay.Response);
            Assert.Single(gateway.Requests);

            var persistedAttempt = await replayContext.PaymentAttempts
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(response.PaymentAttemptId, persistedAttempt.Id);
            Assert.Equal(
                PaymentAttemptStatus.Pending,
                persistedAttempt.Status);
            Assert.Equal("gateway-test", persistedAttempt.Provider);
            Assert.Equal(
                providerPaymentId,
                persistedAttempt.ProviderPaymentId);
            Assert.Null(persistedAttempt.ConfirmedAt);

            var persistedPayment = await replayContext.Payments
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(response.PaymentId, persistedPayment.Id);
            Assert.Equal(PaymentStatus.Pending, persistedPayment.Status);
            Assert.Null(persistedPayment.ConfirmedAt);

            Assert.Equal(
                2,
                await replayContext.IdempotencyRecords.CountAsync());

            Assert.True(
                await replayContext.IdempotencyRecords.AnyAsync(
                    entity =>
                        entity.Operation ==
                            PaymentAttemptCompletionService.Operation &&
                        entity.Key == command.IdempotencyKey));

            var appointment = await replayContext.Appointments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == appointmentId);

            Assert.Equal(AppointmentStatus.Pending, appointment.Status);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]

    public async Task PaymentPreparation_WithConcurrentDifferentKeys_ShouldPrepareOnlyOne()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var now = CreatedAt.AddMinutes(2);

        var firstCommand = new CreatePaymentAttemptCommand(
            appointmentId,
            PaymentMethod.Pix,
            Guid.NewGuid().ToString("N"));

        var secondCommand = new CreatePaymentAttemptCommand(
            appointmentId,
            PaymentMethod.CreditCard,
            Guid.NewGuid().ToString("N"));

        try
        {
            await SeedAsync(data);

            await using (var seedContext = CreateContext(data.TenantId))
            {
                seedContext.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await seedContext.SaveChangesAsync();
            }

            await using var firstContext = CreateContext(data.TenantId);
            await using var secondContext = CreateContext(data.TenantId);

            var firstService = new PaymentAttemptPreparationService(
                firstContext,
                new FixedTimeProvider(now));

            var secondService = new PaymentAttemptPreparationService(
                secondContext,
                new FixedTimeProvider(now));

            var start = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(60));

            async Task<PaymentAttemptPreparationResult> ExecuteAsync(
                PaymentAttemptPreparationService service,
                CreatePaymentAttemptCommand command)
            {
                await start.Task.WaitAsync(timeout.Token);

                return await service.PrepareAsync(
                    command,
                    timeout.Token);
            }

            var firstTask = ExecuteAsync(firstService, firstCommand);
            var secondTask = ExecuteAsync(secondService, secondCommand);

            start.SetResult(true);

            var results = await Task.WhenAll(firstTask, secondTask);

            var success = Assert.Single(
                results,
                result => result.IsSuccess);

            var failure = Assert.Single(
                results,
                result => !result.IsSuccess);

            Assert.Equal(
                CreatePaymentAttemptError.ConcurrencyConflict,
                failure.Error);

            Assert.Null(failure.Request);

            var winningCommand = results[0].IsSuccess
                ? firstCommand
                : secondCommand;

            var losingCommand = results[0].IsSuccess
                ? secondCommand
                : firstCommand;

            var winningRequest =
                Assert.IsType<CreateGatewayPaymentRequest>(
                    success.Request);

            Assert.Null(firstContext.Database.CurrentTransaction);
            Assert.Null(secondContext.Database.CurrentTransaction);

            await using var readContext = CreateContext(data.TenantId);

            var payment = Assert.Single(
                await readContext.Payments
                    .AsNoTracking()
                    .ToListAsync());

            var attempt = Assert.Single(
                await readContext.PaymentAttempts
                    .AsNoTracking()
                    .ToListAsync());

            var intent = Assert.Single(
                await readContext.IdempotencyRecords
                    .AsNoTracking()
                    .ToListAsync());

            Assert.Equal(winningRequest.PaymentId, payment.Id);
            Assert.Equal(winningRequest.PaymentAttemptId, attempt.Id);
            Assert.Equal(payment.Id, attempt.PaymentId);
            Assert.Equal(winningCommand.PaymentMethod, attempt.PaymentMethod);
            Assert.Equal(winningCommand.PaymentMethod, winningRequest.PaymentMethod);

            Assert.Equal(PaymentStatus.Pending, payment.Status);
            Assert.Equal(PaymentAttemptStatus.Created, attempt.Status);
            Assert.Equal(now, attempt.CreatedAt);
            Assert.Null(attempt.ProviderPaymentId);

            Assert.Equal(winningCommand.IdempotencyKey, intent.Key);
            Assert.Equal(
                PaymentAttemptPreparationService.IntentOperation,
                intent.Operation);

            Assert.False(
                await readContext.IdempotencyRecords.AnyAsync(
                    entity => entity.Key == losingCommand.IdempotencyKey));

            var storedRequest =
                System.Text.Json.JsonSerializer
                    .Deserialize<CreateGatewayPaymentRequest>(
                        intent.ResponseJson);

            Assert.Equal(winningRequest, storedRequest);

            // A chave vencedora continua recuperando a mesma tentativa.
            var replayService = new PaymentAttemptPreparationService(
                readContext,
                new FixedTimeProvider(now.AddSeconds(1)));

            var replay = await replayService.PrepareAsync(winningCommand);

            Assert.True(replay.IsSuccess);
            Assert.Equal(winningRequest, replay.Request);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task PaymentPreparation_WithConcurrentSameKey_ShouldReuseSingleAttempt()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var firstNow = CreatedAt.AddMinutes(2);
        var secondNow = firstNow.AddSeconds(1);

        var command = new CreatePaymentAttemptCommand(
            appointmentId,
            PaymentMethod.Pix,
            Guid.NewGuid().ToString("N"));

        try
        {
            await SeedAsync(data);

            await using (var seedContext = CreateContext(data.TenantId))
            {
                seedContext.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await seedContext.SaveChangesAsync();
            }

            await using var firstContext = CreateContext(data.TenantId);
            await using var secondContext = CreateContext(data.TenantId);

            var firstService = new PaymentAttemptPreparationService(
                firstContext,
                new FixedTimeProvider(firstNow));

            var secondService = new PaymentAttemptPreparationService(
                secondContext,
                new FixedTimeProvider(secondNow));

            var start = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(60));

            async Task<PaymentAttemptPreparationResult> ExecuteAsync(
                PaymentAttemptPreparationService service)
            {
                await start.Task.WaitAsync(timeout.Token);

                return await service.PrepareAsync(
                    command,
                    timeout.Token);
            }

            var firstTask = ExecuteAsync(firstService);
            var secondTask = ExecuteAsync(secondService);

            start.SetResult(true);

            var results = await Task.WhenAll(
                firstTask,
                secondTask);

            Assert.All(
                results,
                result => Assert.True(result.IsSuccess));

            var firstRequest =
                Assert.IsType<CreateGatewayPaymentRequest>(
                    results[0].Request);

            var secondRequest =
                Assert.IsType<CreateGatewayPaymentRequest>(
                    results[1].Request);

            Assert.Equal(firstRequest, secondRequest);
            Assert.Equal(
                firstRequest.IdempotencyKey,
                secondRequest.IdempotencyKey);

            Assert.Null(firstContext.Database.CurrentTransaction);
            Assert.Null(secondContext.Database.CurrentTransaction);

            await using var readContext = CreateContext(data.TenantId);

            var payment = Assert.Single(
                await readContext.Payments
                    .AsNoTracking()
                    .ToListAsync());

            var attempt = Assert.Single(
                await readContext.PaymentAttempts
                    .AsNoTracking()
                    .ToListAsync());

            var intent = Assert.Single(
                await readContext.IdempotencyRecords
                    .AsNoTracking()
                    .ToListAsync());

            Assert.Equal(firstRequest.PaymentId, payment.Id);
            Assert.Equal(firstRequest.PaymentAttemptId, attempt.Id);
            Assert.Equal(payment.Id, attempt.PaymentId);

            Assert.Equal(PaymentStatus.Pending, payment.Status);
            Assert.Equal(PaymentAttemptStatus.Created, attempt.Status);
            Assert.Null(attempt.ProviderPaymentId);

            Assert.Contains(
                attempt.CreatedAt,
                new[] { firstNow, secondNow });

            Assert.Equal(attempt.CreatedAt, payment.CreatedAt);
            Assert.Equal(attempt.CreatedAt, intent.CreatedAt);
            Assert.Equal(attempt.CreatedAt, attempt.UpdatedAt);

            Assert.Equal(command.IdempotencyKey, intent.Key);
            Assert.Equal(
                PaymentAttemptPreparationService.IntentOperation,
                intent.Operation);

            var storedRequest =
                System.Text.Json.JsonSerializer
                    .Deserialize<CreateGatewayPaymentRequest>(
                        intent.ResponseJson);

            Assert.Equal(firstRequest, storedRequest);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task PaymentPreparation_ShouldIsolateAppointmentsAndIntentKeysBetweenTenants()
    {
        var owner = new TestData();
        var other = new TestData();
        var ownerAppointmentId = Guid.NewGuid();
        var otherAppointmentId = Guid.NewGuid();
        var sharedKey = Guid.NewGuid().ToString("N");
        var now = CreatedAt.AddMinutes(2);

        var ownerCommand = new CreatePaymentAttemptCommand(
            ownerAppointmentId,
            PaymentMethod.Pix,
            sharedKey);

        try
        {
            await SeedAsync(owner);
            await SeedAsync(other);

            await using (var context = CreateContext(owner.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        owner,
                        ownerAppointmentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(other.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        other,
                        otherAppointmentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            CreateGatewayPaymentRequest ownerRequest;

            await using (var context = CreateContext(owner.TenantId))
            {
                var service = new PaymentAttemptPreparationService(
                    context,
                    new FixedTimeProvider(now));

                var result = await service.PrepareAsync(ownerCommand);

                Assert.True(result.IsSuccess);

                ownerRequest =
                    Assert.IsType<CreateGatewayPaymentRequest>(
                        result.Request);
            }

            await using (var context = CreateContext(other.TenantId))
            {
                var service = new PaymentAttemptPreparationService(
                    context,
                    new FixedTimeProvider(now));

                // Mesmo AppointmentId e mesma chave do proprietário.
                var foreignResult = await service.PrepareAsync(
                    ownerCommand);

                Assert.False(foreignResult.IsSuccess);
                Assert.Equal(
                    CreatePaymentAttemptError.AppointmentNotFound,
                    foreignResult.Error);
                Assert.Null(foreignResult.Request);

                Assert.Equal(0, await context.Payments.CountAsync());
                Assert.Equal(0, await context.PaymentAttempts.CountAsync());
                Assert.Equal(0, await context.IdempotencyRecords.CountAsync());
                Assert.Null(context.Database.CurrentTransaction);
            }

            var otherCommand = new CreatePaymentAttemptCommand(
                otherAppointmentId,
                PaymentMethod.CreditCard,
                sharedKey);

            CreateGatewayPaymentRequest otherRequest;

            await using (var context = CreateContext(other.TenantId))
            {
                var service = new PaymentAttemptPreparationService(
                    context,
                    new FixedTimeProvider(now));

                var result = await service.PrepareAsync(otherCommand);

                Assert.True(result.IsSuccess);

                otherRequest =
                    Assert.IsType<CreateGatewayPaymentRequest>(
                        result.Request);

                Assert.Equal(other.TenantId, otherRequest.TenantId);
                Assert.Equal(otherAppointmentId, otherRequest.AppointmentId);
                Assert.Equal(
                    PaymentMethod.CreditCard,
                    otherRequest.PaymentMethod);

                Assert.NotEqual(
                    ownerRequest.PaymentId,
                    otherRequest.PaymentId);

                Assert.NotEqual(
                    ownerRequest.PaymentAttemptId,
                    otherRequest.PaymentAttemptId);

                Assert.NotEqual(
                    ownerRequest.IdempotencyKey,
                    otherRequest.IdempotencyKey);

                var intent = Assert.Single(
                    await context.IdempotencyRecords
                        .AsNoTracking()
                        .ToListAsync());

                Assert.Equal(other.TenantId, intent.TenantId);
                Assert.Equal(sharedKey, intent.Key);

                var storedRequest =
                    System.Text.Json.JsonSerializer
                        .Deserialize<CreateGatewayPaymentRequest>(
                            intent.ResponseJson);

                Assert.Equal(otherRequest, storedRequest);
                Assert.Equal(1, await context.Payments.CountAsync());
                Assert.Equal(1, await context.PaymentAttempts.CountAsync());
            }

            // A operação do outro tenant não altera a intenção original.
            await using (var context = CreateContext(owner.TenantId))
            {
                var service = new PaymentAttemptPreparationService(
                    context,
                    new FixedTimeProvider(now.AddMinutes(1)));

                var replay = await service.PrepareAsync(ownerCommand);

                Assert.True(replay.IsSuccess);
                Assert.Equal(ownerRequest, replay.Request);

                var intent = Assert.Single(
                    await context.IdempotencyRecords
                        .AsNoTracking()
                        .ToListAsync());

                Assert.Equal(owner.TenantId, intent.TenantId);
                Assert.Equal(sharedKey, intent.Key);

                Assert.Equal(1, await context.Payments.CountAsync());
                Assert.Equal(1, await context.PaymentAttempts.CountAsync());
            }
        }
        finally
        {
            try
            {
                await CleanupAsync(owner.TenantId);
            }
            finally
            {
                await CleanupAsync(other.TenantId);
            }
        }
    }

    [Fact]
    public async Task PaymentPreparation_WithFailureBeforeCommit_ShouldRollbackAndAllowRetry()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var now = CreatedAt.AddMinutes(2);

        var command = new CreatePaymentAttemptCommand(
            appointmentId,
            PaymentMethod.Pix,
            Guid.NewGuid().ToString("N"));

        try
        {
            await SeedAsync(data);

            await using (var seedContext = CreateContext(data.TenantId))
            {
                seedContext.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await seedContext.SaveChangesAsync();
            }

            var interceptor = new FailAfterPaymentPreparationSaveInterceptor();

            await using (var failingContext = CreateContext(
                data.TenantId,
                interceptor))
            {
                var service = new PaymentAttemptPreparationService(
                    failingContext,
                    new FixedTimeProvider(now));

                var exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => service.PrepareAsync(command));

                Assert.Equal(
                    "Simulated failure before payment preparation commit.",
                    exception.Message);

                Assert.Equal(3, interceptor.SavedEntriesCount);
                Assert.True(interceptor.HadActiveTransaction);

                Assert.Null(failingContext.Database.CurrentTransaction);

                Assert.Empty(
                    failingContext.ChangeTracker.Entries<Payment>());

                Assert.Empty(
                    failingContext.ChangeTracker.Entries<PaymentAttempt>());

                Assert.Empty(
                    failingContext.ChangeTracker
                        .Entries<VirtualEmployee.Infrastructure.Persistence.Idempotency.IdempotencyRecord>());
            }

            // Outro contexto comprova que nada foi confirmado no banco.
            await using (var readContext = CreateContext(data.TenantId))
            {
                Assert.Equal(
                    0,
                    await readContext.Payments.CountAsync());

                Assert.Equal(
                    0,
                    await readContext.PaymentAttempts.CountAsync());

                Assert.Equal(
                    0,
                    await readContext.IdempotencyRecords.CountAsync());

                Assert.True(
                    await readContext.Appointments.AnyAsync(
                        entity => entity.Id == appointmentId));
            }

            // A chave não fica consumida pela transação revertida.
            await using (var retryContext = CreateContext(data.TenantId))
            {
                var service = new PaymentAttemptPreparationService(
                    retryContext,
                    new FixedTimeProvider(now.AddMinutes(1)));

                var retry = await service.PrepareAsync(command);

                Assert.True(retry.IsSuccess);

                var request =
                    Assert.IsType<CreateGatewayPaymentRequest>(
                        retry.Request);

                var payment = Assert.Single(
                    await retryContext.Payments
                        .AsNoTracking()
                        .ToListAsync());

                var attempt = Assert.Single(
                    await retryContext.PaymentAttempts
                        .AsNoTracking()
                        .ToListAsync());

                var intent = Assert.Single(
                    await retryContext.IdempotencyRecords
                        .AsNoTracking()
                        .ToListAsync());

                Assert.Equal(request.PaymentId, payment.Id);
                Assert.Equal(request.PaymentAttemptId, attempt.Id);
                Assert.Equal(payment.Id, attempt.PaymentId);
                Assert.Equal(command.IdempotencyKey, intent.Key);
                Assert.Equal(PaymentAttemptStatus.Created, attempt.Status);
                Assert.Null(retryContext.Database.CurrentTransaction);
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task PaymentPreparation_ShouldPersistIntentAndReuseItAcrossContexts()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var now = CreatedAt.AddMinutes(2);

        var command = new CreatePaymentAttemptCommand(
            appointmentId,
            PaymentMethod.Pix,
            Guid.NewGuid().ToString("N"));

        try
        {
            await SeedAsync(data);

            await using (var seedContext = CreateContext(data.TenantId))
            {
                seedContext.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await seedContext.SaveChangesAsync();
            }

            CreateGatewayPaymentRequest originalRequest;

            await using (var firstContext = CreateContext(data.TenantId))
            {
                var service = new PaymentAttemptPreparationService(
                    firstContext,
                    new FixedTimeProvider(now));

                var result = await service.PrepareAsync(command);

                Assert.True(result.IsSuccess);
                Assert.Equal(CreatePaymentAttemptError.None, result.Error);

                originalRequest =
                    Assert.IsType<CreateGatewayPaymentRequest>(
                        result.Request);

                Assert.Null(firstContext.Database.CurrentTransaction);
            }

            await using (var readContext = CreateContext(data.TenantId))
            {
                var payment = Assert.Single(
                    await readContext.Payments
                        .AsNoTracking()
                        .ToListAsync());

                var attempt = Assert.Single(
                    await readContext.PaymentAttempts
                        .AsNoTracking()
                        .ToListAsync());

                var intent = Assert.Single(
                    await readContext.IdempotencyRecords
                        .AsNoTracking()
                        .ToListAsync());

                Assert.Equal(data.TenantId, payment.TenantId);
                Assert.Equal(data.BusinessId, payment.BusinessId);
                Assert.Equal(appointmentId, payment.AppointmentId);
                Assert.Equal(PaymentStatus.Pending, payment.Status);

                Assert.Equal(payment.Id, attempt.PaymentId);
                Assert.Equal(payment.Amount, attempt.Amount);
                Assert.Equal(PaymentAttemptStatus.Created, attempt.Status);
                Assert.Equal(PaymentMethod.Pix, attempt.PaymentMethod);
                Assert.Equal(now, attempt.CreatedAt);
                Assert.Null(attempt.Provider);
                Assert.Null(attempt.ProviderPaymentId);

                Assert.Equal(
                    PaymentAttemptPreparationService.IntentOperation,
                    intent.Operation);

                Assert.Equal(command.IdempotencyKey, intent.Key);
                Assert.Equal(64, intent.RequestHash.Length);

                var storedRequest =
                    System.Text.Json.JsonSerializer
                        .Deserialize<CreateGatewayPaymentRequest>(
                            intent.ResponseJson);

                Assert.NotNull(storedRequest);
                Assert.Equal(originalRequest, storedRequest);
                Assert.Equal(payment.Id, originalRequest.PaymentId);
                Assert.Equal(attempt.Id, originalRequest.PaymentAttemptId);
                Assert.Equal(payment.Amount, originalRequest.Amount);
                Assert.Equal(attempt.ExpiresAt, originalRequest.ExpiresAt);
            }

            // Outra instância, outro contexto e relógio posterior.
            await using (var replayContext = CreateContext(data.TenantId))
            {
                var service = new PaymentAttemptPreparationService(
                    replayContext,
                    new FixedTimeProvider(now.AddMinutes(1)));

                var replay = await service.PrepareAsync(command);

                Assert.True(replay.IsSuccess);

                var replayRequest =
                    Assert.IsType<CreateGatewayPaymentRequest>(
                        replay.Request);

                Assert.Equal(originalRequest, replayRequest);
                Assert.Equal(
                    originalRequest.IdempotencyKey,
                    replayRequest.IdempotencyKey);

                Assert.Null(replayContext.Database.CurrentTransaction);

                Assert.Equal(
                    1,
                    await replayContext.Payments.CountAsync());

                Assert.Equal(
                    1,
                    await replayContext.PaymentAttempts.CountAsync());

                Assert.Equal(
                    1,
                    await replayContext.IdempotencyRecords.CountAsync());

                var attempt = await replayContext.PaymentAttempts
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal(now, attempt.CreatedAt);
                Assert.Equal(now, attempt.UpdatedAt);
                Assert.Equal(PaymentAttemptStatus.Created, attempt.Status);
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task PaymentAttempt_WithStaleFailure_ShouldPreserveConfirmation()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var providerPaymentId = Guid.NewGuid().ToString("N");

        try
        {
            await SeedAsync(data);

            await using var context = CreateContext(data.TenantId);
            await using var transaction =
                await context.Database.BeginTransactionAsync();

            var appointment = CreateAppointment(
                data,
                appointmentId,
                CreatedAt.AddHours(2));

            context.Appointments.Add(appointment);
            await context.SaveChangesAsync();

            var payment = new Payment(
                paymentId,
                appointment,
                CreatedAt.AddMinutes(1));

            context.Payments.Add(payment);
            await context.SaveChangesAsync();

            var attempt = new PaymentAttempt(
                attemptId,
                payment,
                PaymentMethod.Pix,
                CreatedAt.AddMinutes(1),
                CreatedAt.AddMinutes(10));

            attempt.RegisterProviderPayment(
                "gateway-test",
                providerPaymentId,
                CreatedAt.AddMinutes(2));

            context.PaymentAttempts.Add(attempt);
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();

            await using var staleContext = CreateContext(data.TenantId);

            staleContext.Database.SetDbConnection(
                context.Database.GetDbConnection());

            await staleContext.Database.UseTransactionAsync(
                transaction.GetDbTransaction());

            var winningAttempt = await context.PaymentAttempts
                .SingleAsync(entity => entity.Id == attemptId);

            var staleAttempt = await staleContext.PaymentAttempts
                .SingleAsync(entity => entity.Id == attemptId);

            Assert.Equal(
                PaymentAttemptStatus.Pending,
                winningAttempt.Status);

            Assert.Equal(
                PaymentAttemptStatus.Pending,
                staleAttempt.Status);

            var confirmedAt = CreatedAt.AddMinutes(3);

            Assert.True(winningAttempt.Confirm(
                payment.Amount,
                confirmedAt));

            await context.SaveChangesAsync();

            // Este objeto desconhece a confirmação já persistida.
            Assert.True(staleAttempt.Fail(
                CreatedAt.AddMinutes(4)));

            var exception =
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                    () => staleContext.SaveChangesAsync());

            var conflictingEntry = Assert.Single(exception.Entries);
            Assert.Same(staleAttempt, conflictingEntry.Entity);

            staleContext.ChangeTracker.Clear();
            context.ChangeTracker.Clear();

            var persistedAttempt = await context.PaymentAttempts
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == attemptId);

            Assert.Equal(
                PaymentAttemptStatus.Confirmed,
                persistedAttempt.Status);

            Assert.Equal(confirmedAt, persistedAttempt.ConfirmedAt);
            Assert.Equal(confirmedAt, persistedAttempt.UpdatedAt);
            Assert.Equal("gateway-test", persistedAttempt.Provider);
            Assert.Equal(providerPaymentId, persistedAttempt.ProviderPaymentId);
            Assert.Equal(payment.Amount, persistedAttempt.Amount);

            // A liquidação do Payment será coordenada pelo serviço.
            var persistedPayment = await context.Payments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == paymentId);

            Assert.Equal(PaymentStatus.Pending, persistedPayment.Status);
            Assert.Null(persistedPayment.ConfirmedPaymentAttemptId);

            await transaction.RollbackAsync();
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task Payment_WithStaleConfirmation_ShouldPreserveWinningAttempt()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var firstAttemptId = Guid.NewGuid();
        var secondAttemptId = Guid.NewGuid();

        try
        {
            await SeedAsync(data);

            await using var context = CreateContext(data.TenantId);
            await using var transaction =
                await context.Database.BeginTransactionAsync();

            var appointment = CreateAppointment(
                data,
                appointmentId,
                CreatedAt.AddHours(2));

            context.Appointments.Add(appointment);
            await context.SaveChangesAsync();

            var payment = new Payment(
                paymentId,
                appointment,
                CreatedAt.AddMinutes(1));

            context.Payments.Add(payment);
            await context.SaveChangesAsync();

            var firstAttempt = new PaymentAttempt(
                firstAttemptId,
                payment,
                PaymentMethod.Pix,
                CreatedAt.AddMinutes(1),
                CreatedAt.AddMinutes(10));

            var secondAttempt = new PaymentAttempt(
                secondAttemptId,
                payment,
                PaymentMethod.CreditCard,
                CreatedAt.AddMinutes(1),
                CreatedAt.AddMinutes(10));

            firstAttempt.RegisterProviderPayment(
                "gateway-test",
                Guid.NewGuid().ToString("N"),
                CreatedAt.AddMinutes(1));

            secondAttempt.RegisterProviderPayment(
                "gateway-test",
                Guid.NewGuid().ToString("N"),
                CreatedAt.AddMinutes(1));

            firstAttempt.Confirm(
                payment.Amount,
                CreatedAt.AddMinutes(2));

            secondAttempt.Confirm(
                payment.Amount,
                CreatedAt.AddMinutes(2));

            context.PaymentAttempts.AddRange(
                firstAttempt,
                secondAttempt);

            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            await using var staleContext = CreateContext(data.TenantId);

            staleContext.Database.SetDbConnection(
                context.Database.GetDbConnection());

            await staleContext.Database.UseTransactionAsync(
                transaction.GetDbTransaction());

            // Os dois contextos carregam a mesma versão PENDING.
            var winningPayment = await context.Payments
                .SingleAsync(entity => entity.Id == paymentId);

            var stalePayment = await staleContext.Payments
                .SingleAsync(entity => entity.Id == paymentId);

            Assert.Equal(PaymentStatus.Pending, winningPayment.Status);
            Assert.Equal(PaymentStatus.Pending, stalePayment.Status);

            var winningTime = CreatedAt.AddMinutes(3);

            Assert.True(winningPayment.Confirm(
                firstAttempt,
                winningTime));

            await context.SaveChangesAsync();

            // O segundo objeto ainda possui os tokens originais.
            Assert.True(stalePayment.Confirm(
                secondAttempt,
                CreatedAt.AddMinutes(4)));

            var exception =
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                    () => staleContext.SaveChangesAsync());

            var conflictingEntry = Assert.Single(exception.Entries);
            Assert.Same(stalePayment, conflictingEntry.Entity);

            staleContext.ChangeTracker.Clear();
            context.ChangeTracker.Clear();

            var persistedPayment = await context.Payments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == paymentId);

            Assert.Equal(PaymentStatus.Confirmed, persistedPayment.Status);
            Assert.Equal(
                firstAttemptId,
                persistedPayment.ConfirmedPaymentAttemptId);
            Assert.Equal(winningTime, persistedPayment.ConfirmedAt);
            Assert.Equal(winningTime, persistedPayment.UpdatedAt);

            // Ambas as cobranças recebidas continuam registradas.
            // A compensação da excedente será tratada pela Application.
            var persistedAttempts = await context.PaymentAttempts
                .AsNoTracking()
                .Where(entity => entity.PaymentId == paymentId)
                .ToListAsync();

            Assert.Equal(2, persistedAttempts.Count);
            Assert.All(
                persistedAttempts,
                entity => Assert.Equal(
                    PaymentAttemptStatus.Confirmed,
                    entity.Status));

            await transaction.RollbackAsync();
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task Payment_WithConfirmationFromAnotherPayment_ShouldBeRejected()
    {
        var data = new TestData();

        var firstAppointmentId = Guid.NewGuid();
        var secondAppointmentId = Guid.NewGuid();
        var firstPaymentId = Guid.NewGuid();
        var secondPaymentId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        try
        {
            await SeedAsync(data);

            await using var context = CreateContext(data.TenantId);
            await using var transaction =
                await context.Database.BeginTransactionAsync();

            // Intervalos separados para não acionar a exclusão de agenda.
            var firstAppointment = CreateAppointment(
                data,
                firstAppointmentId,
                CreatedAt.AddHours(2));

            var secondAppointment = CreateAppointment(
                data,
                secondAppointmentId,
                CreatedAt.AddHours(4));

            context.Appointments.AddRange(
                firstAppointment,
                secondAppointment);

            await context.SaveChangesAsync();

            var firstPayment = new Payment(
                firstPaymentId,
                firstAppointment,
                CreatedAt.AddMinutes(1));

            var secondPayment = new Payment(
                secondPaymentId,
                secondAppointment,
                CreatedAt.AddMinutes(1));

            context.Payments.AddRange(firstPayment, secondPayment);
            await context.SaveChangesAsync();

            var attempt = new PaymentAttempt(
                attemptId,
                secondPayment,
                PaymentMethod.Pix,
                CreatedAt.AddMinutes(1),
                CreatedAt.AddMinutes(10));

            attempt.RegisterProviderPayment(
                "gateway-test",
                Guid.NewGuid().ToString("N"),
                CreatedAt.AddMinutes(2));

            attempt.Confirm(
                secondPayment.Amount,
                CreatedAt.AddMinutes(3));

            context.PaymentAttempts.Add(attempt);
            await context.SaveChangesAsync();

            var confirmedAt = CreatedAt.AddMinutes(4);

            // Ignora o domínio propositalmente para testar a FK do banco.
            var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(
                async () =>
                {
                    await context.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        UPDATE payments
                        SET status = 'CONFIRMED',
                            confirmed_payment_attempt_id = {attemptId},
                            confirmed_at = {confirmedAt},
                            updated_at = {confirmedAt}
                        WHERE tenant_id = {data.TenantId}
                          AND id = {firstPaymentId};
                        """);
                });

            Assert.Equal(
                Npgsql.PostgresErrorCodes.ForeignKeyViolation,
                exception.SqlState);

            Assert.Equal(
                "FK_payments_payment_attempts_tenant_id_id_confirmed_payment_at~",
                exception.ConstraintName);

            await transaction.RollbackAsync();

            Assert.False(
                await context.Payments
                    .AsNoTracking()
                    .AnyAsync(entity => entity.Id == firstPaymentId));
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task PaymentAndAttempt_ShouldBeIsolatedBetweenTenants()
    {
        var owner = new TestData();
        var other = new TestData();

        var appointmentId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        try
        {
            await SeedAsync(owner);
            await SeedAsync(other);

            await using var ownerContext = CreateContext(owner.TenantId);
            await using var transaction =
                await ownerContext.Database.BeginTransactionAsync();

            var appointment = CreateAppointment(
                owner,
                appointmentId,
                CreatedAt.AddHours(2));

            ownerContext.Appointments.Add(appointment);
            await ownerContext.SaveChangesAsync();

            var payment = new Payment(
                paymentId,
                appointment,
                CreatedAt.AddMinutes(1));

            ownerContext.Payments.Add(payment);
            await ownerContext.SaveChangesAsync();

            var attempt = new PaymentAttempt(
                attemptId,
                payment,
                PaymentMethod.Pix,
                CreatedAt.AddMinutes(1),
                CreatedAt.AddMinutes(10));

            ownerContext.PaymentAttempts.Add(attempt);
            await ownerContext.SaveChangesAsync();

            Assert.True(
                await ownerContext.Payments
                    .AnyAsync(entity => entity.Id == paymentId));

            Assert.True(
                await ownerContext.PaymentAttempts
                    .AnyAsync(entity => entity.Id == attemptId));

            await using var otherContext = CreateContext(other.TenantId);

            // Compartilha a transação para enxergar os dados ainda
            // não confirmados, mantendo outro contexto de tenant.
            otherContext.Database.SetDbConnection(
                ownerContext.Database.GetDbConnection());

            await otherContext.Database.UseTransactionAsync(
                transaction.GetDbTransaction());

            Assert.False(
                await otherContext.Payments
                    .AnyAsync(entity => entity.Id == paymentId));

            Assert.False(
                await otherContext.PaymentAttempts
                    .AnyAsync(entity => entity.Id == attemptId));

            Assert.Equal(
                0,
                await otherContext.Payments
                    .Where(entity => entity.Id == paymentId)
                    .CountAsync());

            Assert.Equal(
                0,
                await otherContext.PaymentAttempts
                    .Where(entity => entity.Id == attemptId)
                    .CountAsync());

            // Prova que os registros existem na mesma transação
            // e são ocultados pelos filtros de tenant.
            var physicalPayment = await otherContext.Payments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == paymentId);

            var physicalAttempt = await otherContext.PaymentAttempts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == attemptId);

            Assert.Equal(owner.TenantId, physicalPayment.TenantId);
            Assert.Equal(owner.TenantId, physicalAttempt.TenantId);

            // O contexto de outro tenant também não pode inserir
            // entidades financeiras pertencentes ao proprietário.
            otherContext.Payments.Add(physicalPayment);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => otherContext.SaveChangesAsync());

            otherContext.ChangeTracker.Clear();

            otherContext.PaymentAttempts.Add(physicalAttempt);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => otherContext.SaveChangesAsync());

            otherContext.ChangeTracker.Clear();

            await transaction.RollbackAsync();
        }
        finally
        {
            try
            {
                await CleanupAsync(owner.TenantId);
            }
            finally
            {
                await CleanupAsync(other.TenantId);
            }
        }
    }

    [Fact]
    public async Task PaymentAttempt_WithDuplicateProviderPayment_ShouldRejectSecondAttempt()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var firstAttemptId = Guid.NewGuid();
        var secondAttemptId = Guid.NewGuid();
        var providerPaymentId = Guid.NewGuid().ToString("N");

        try
        {
            await SeedAsync(data);

            await using var context = CreateContext(data.TenantId);
            await using var transaction =
                await context.Database.BeginTransactionAsync();

            var appointment = CreateAppointment(
                data,
                appointmentId,
                CreatedAt.AddHours(2));

            context.Appointments.Add(appointment);
            await context.SaveChangesAsync();

            var payment = new Payment(
                paymentId,
                appointment,
                CreatedAt.AddMinutes(1));

            context.Payments.Add(payment);
            await context.SaveChangesAsync();

            var firstAttempt = new PaymentAttempt(
                firstAttemptId,
                payment,
                PaymentMethod.Pix,
                CreatedAt.AddMinutes(1),
                CreatedAt.AddMinutes(10));

            firstAttempt.RegisterProviderPayment(
                "gateway-test",
                providerPaymentId,
                CreatedAt.AddMinutes(2));

            context.PaymentAttempts.Add(firstAttempt);
            await context.SaveChangesAsync();

            var secondAttempt = new PaymentAttempt(
                secondAttemptId,
                payment,
                PaymentMethod.CreditCard,
                CreatedAt.AddMinutes(3),
                CreatedAt.AddMinutes(10));

            secondAttempt.RegisterProviderPayment(
                "gateway-test",
                providerPaymentId,
                CreatedAt.AddMinutes(3));

            context.PaymentAttempts.Add(secondAttempt);

            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => context.SaveChangesAsync());

            var postgresException =
                Assert.IsType<Npgsql.PostgresException>(
                    exception.InnerException);

            Assert.Equal(
                Npgsql.PostgresErrorCodes.UniqueViolation,
                postgresException.SqlState);

            Assert.Equal(
                "IX_payment_attempts_provider_provider_payment_id",
                postgresException.ConstraintName);

            context.ChangeTracker.Clear();

            var persistedAttempt = Assert.Single(
                await context.PaymentAttempts
                    .AsNoTracking()
                    .Where(attempt => attempt.PaymentId == paymentId)
                    .ToListAsync());

            Assert.Equal(firstAttemptId, persistedAttempt.Id);
            Assert.Equal(providerPaymentId, persistedAttempt.ProviderPaymentId);
            Assert.Equal(PaymentMethod.Pix, persistedAttempt.PaymentMethod);

            Assert.False(
                await context.PaymentAttempts
                    .AnyAsync(attempt => attempt.Id == secondAttemptId));

            await transaction.RollbackAsync();
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task Payment_WithDuplicateAppointment_ShouldRejectSecondPayment()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var firstPaymentId = Guid.NewGuid();
        var secondPaymentId = Guid.NewGuid();

        try
        {
            await SeedAsync(data);

            await using var context = CreateContext(data.TenantId);
            await using var transaction =
                await context.Database.BeginTransactionAsync();

            var appointment = CreateAppointment(
                data,
                appointmentId,
                CreatedAt.AddHours(2));

            context.Appointments.Add(appointment);
            await context.SaveChangesAsync();

            context.Payments.Add(
                new Payment(
                    firstPaymentId,
                    appointment,
                    CreatedAt.AddMinutes(1)));

            await context.SaveChangesAsync();

            context.Payments.Add(
                new Payment(
                    secondPaymentId,
                    appointment,
                    CreatedAt.AddMinutes(2)));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => context.SaveChangesAsync());

            var postgresException =
                Assert.IsType<Npgsql.PostgresException>(
                    exception.InnerException);

            Assert.Equal(
                Npgsql.PostgresErrorCodes.UniqueViolation,
                postgresException.SqlState);

            Assert.Equal(
                "IX_payments_tenant_id_appointment_id",
                postgresException.ConstraintName);

            // O EF usa savepoint dentro da transação existente.
            // Remove o objeto cuja inserção foi rejeitada antes da leitura.
            context.ChangeTracker.Clear();

            var persistedPayment = Assert.Single(
                await context.Payments
                    .AsNoTracking()
                    .Where(payment =>
                        payment.AppointmentId == appointmentId)
                    .ToListAsync());

            Assert.Equal(firstPaymentId, persistedPayment.Id);
            Assert.Equal(PaymentStatus.Pending, persistedPayment.Status);

            Assert.False(
                await context.Payments
                    .AnyAsync(payment => payment.Id == secondPaymentId));

            await transaction.RollbackAsync();
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task PaymentAndAttempt_ShouldPersistAndConfirmWithTenantRelationships()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var providerPaymentId = Guid.NewGuid().ToString("N");

        var paymentCreatedAt = CreatedAt.AddMinutes(1);
        var registeredAt = CreatedAt.AddMinutes(2);
        var confirmedAt = CreatedAt.AddMinutes(3);

        try
        {
            await SeedAsync(data);

            await using var context = CreateContext(data.TenantId);

            // Todas as alterações financeiras deste teste serão revertidas.
            await using var transaction =
                await context.Database.BeginTransactionAsync();

            var appointment = CreateAppointment(
                data,
                appointmentId,
                CreatedAt.AddHours(2));

            context.Appointments.Add(appointment);
            await context.SaveChangesAsync();

            var payment = new Payment(
                paymentId,
                appointment,
                paymentCreatedAt);

            context.Payments.Add(payment);
            await context.SaveChangesAsync();

            var attempt = new PaymentAttempt(
                attemptId,
                payment,
                PaymentMethod.Pix,
                paymentCreatedAt,
                CreatedAt.AddMinutes(10));

            context.PaymentAttempts.Add(attempt);
            await context.SaveChangesAsync();

            attempt.RegisterProviderPayment(
                "gateway-test",
                providerPaymentId,
                registeredAt);

            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();

            var persistedPayment = await context.Payments
                .SingleAsync(entity => entity.Id == paymentId);

            var persistedAttempt = await context.PaymentAttempts
                .SingleAsync(entity => entity.Id == attemptId);

            Assert.Equal(data.TenantId, persistedPayment.TenantId);
            Assert.Equal(data.BusinessId, persistedPayment.BusinessId);
            Assert.Equal(appointmentId, persistedPayment.AppointmentId);
            Assert.Equal(appointment.TotalPriceSnapshot, persistedPayment.Amount);
            Assert.Equal(PaymentStatus.Pending, persistedPayment.Status);
            Assert.Null(persistedPayment.ConfirmedPaymentAttemptId);
            Assert.Null(persistedPayment.ConfirmedAt);

            Assert.Equal(data.TenantId, persistedAttempt.TenantId);
            Assert.Equal(paymentId, persistedAttempt.PaymentId);
            Assert.Equal(persistedPayment.Amount, persistedAttempt.Amount);
            Assert.Equal(PaymentMethod.Pix, persistedAttempt.PaymentMethod);
            Assert.Equal(PaymentAttemptStatus.Pending, persistedAttempt.Status);
            Assert.Equal("gateway-test", persistedAttempt.Provider);
            Assert.Equal(providerPaymentId, persistedAttempt.ProviderPaymentId);
            Assert.Equal(registeredAt, persistedAttempt.UpdatedAt);

            Assert.True(persistedAttempt.Confirm(
                persistedPayment.Amount,
                confirmedAt));

            // Persiste a tentativa confirmada antes de liquidar o Payment.
            await context.SaveChangesAsync();

            Assert.True(persistedPayment.Confirm(
                persistedAttempt,
                confirmedAt));

            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();

            var confirmedPayment = await context.Payments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == paymentId);

            var confirmedAttempt = await context.PaymentAttempts
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == attemptId);

            Assert.Equal(PaymentStatus.Confirmed, confirmedPayment.Status);
            Assert.Equal(attemptId, confirmedPayment.ConfirmedPaymentAttemptId);
            Assert.Equal(confirmedAt, confirmedPayment.ConfirmedAt);
            Assert.Equal(confirmedAt, confirmedPayment.UpdatedAt);
            Assert.Equal(paymentCreatedAt, confirmedPayment.CreatedAt);

            Assert.Equal(PaymentAttemptStatus.Confirmed, confirmedAttempt.Status);
            Assert.Equal(confirmedAt, confirmedAttempt.ConfirmedAt);
            Assert.Equal(confirmedAt, confirmedAttempt.UpdatedAt);
            Assert.Equal(providerPaymentId, confirmedAttempt.ProviderPaymentId);
            Assert.Equal(confirmedPayment.Amount, confirmedAttempt.Amount);

            Assert.Equal(TimeSpan.Zero, confirmedPayment.ConfirmedAt!.Value.Offset);
            Assert.Equal(TimeSpan.Zero, confirmedAttempt.ConfirmedAt!.Value.Offset);

            // O domínio financeiro ainda não altera o estado operacional.
            var persistedAppointment = await context.Appointments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == appointmentId);

            Assert.Equal(
                AppointmentStatus.Pending,
                persistedAppointment.Status);

            await transaction.RollbackAsync();
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentHistory_ShouldIsolateWorkerExpirationBetweenTenants()
    {
        var current = new TestData();
        var other = new TestData();

        var currentAppointmentId = Guid.NewGuid();
        var otherAppointmentId = Guid.NewGuid();

        var startsAt = new DateTimeOffset(
            2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        var expiresAt = CreatedAt.AddMinutes(10);

        try
        {
            await SeedBookableAsync(current);
            await SeedBookableAsync(other);

            await using (var context = CreateContext(current.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        current,
                        currentAppointmentId,
                        startsAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(other.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        other,
                        otherAppointmentId,
                        startsAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(current.TenantId))
            {
                var service = new AppointmentExpirationService(context);

                Assert.Equal(
                    1,
                    await service.ExpireDueAsync(expiresAt));
            }

            await using (var context = CreateContext(current.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(
                        entity => entity.Id == currentAppointmentId);

                Assert.Equal(AppointmentStatus.Expired, appointment.Status);
                Assert.Equal(expiresAt, appointment.UpdatedAt);

                var history = Assert.Single(
                    await context.AppointmentHistoryEntries
                        .AsNoTracking()
                        .ToListAsync());

                Assert.Equal(current.TenantId, history.TenantId);
                Assert.Equal(currentAppointmentId, history.AppointmentId);
                Assert.Equal(AppointmentHistoryType.Expired, history.Type);
            }

            await using (var context = CreateContext(other.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(
                        entity => entity.Id == otherAppointmentId);

                Assert.Equal(AppointmentStatus.Pending, appointment.Status);
                Assert.Equal(CreatedAt, appointment.UpdatedAt);
                Assert.Equal(expiresAt, appointment.ReservationExpiresAt);

                Assert.False(
                    await context.AppointmentHistoryEntries.AnyAsync());
            }

            // O segundo tenant só é alterado pelo seu próprio ciclo.
            await using (var context = CreateContext(other.TenantId))
            {
                var service = new AppointmentExpirationService(context);

                Assert.Equal(
                    1,
                    await service.ExpireDueAsync(expiresAt));
            }

            await using (var context = CreateContext(other.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(
                        entity => entity.Id == otherAppointmentId);

                Assert.Equal(AppointmentStatus.Expired, appointment.Status);

                var history = Assert.Single(
                    await context.AppointmentHistoryEntries
                        .AsNoTracking()
                        .ToListAsync());

                Assert.Equal(other.TenantId, history.TenantId);
                Assert.Equal(otherAppointmentId, history.AppointmentId);
                Assert.Equal(AppointmentHistoryType.Expired, history.Type);
            }
        }
        finally
        {
            try
            {
                await CleanupAsync(current.TenantId);
            }
            finally
            {
                await CleanupAsync(other.TenantId);
            }
        }
    }

    [Fact]
    public async Task AppointmentHistory_ShouldRecordConcurrentWorkerExpirationOnlyOnce()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var startsAt = new DateTimeOffset(
            2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        var expiresAt = CreatedAt.AddMinutes(10);

        try
        {
            await SeedBookableAsync(data);

            AppointmentHistoryState initialState;

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = CreateAppointment(
                    data,
                    appointmentId,
                    startsAt);

                initialState = AppointmentHistoryState.Capture(appointment);

                context.Appointments.Add(appointment);
                await context.SaveChangesAsync();
            }

            await using var firstContext = CreateContext(data.TenantId);
            await using var secondContext = CreateContext(data.TenantId);

            var firstService = new AppointmentExpirationService(firstContext);
            var secondService = new AppointmentExpirationService(secondContext);

            var start = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(30));

            async Task<int> ExecuteAsync(
                AppointmentExpirationService service)
            {
                await start.Task.WaitAsync(timeout.Token);

                return await service.ExpireDueAsync(
                    expiresAt,
                    timeout.Token);
            }

            var firstTask = ExecuteAsync(firstService);
            var secondTask = ExecuteAsync(secondService);

            start.SetResult(true);

            var results = await Task.WhenAll(firstTask, secondTask);

            Assert.Equal(new[] { 0, 1 }, results.OrderBy(value => value).ToArray());

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == appointmentId);

                var entries = await context.AppointmentHistoryEntries
                    .AsNoTracking()
                    .Where(entry => entry.AppointmentId == appointmentId)
                    .ToListAsync();

                var expiration = Assert.Single(entries);

                Assert.Equal(
                    AppointmentHistoryType.Expired,
                    expiration.Type);
                Assert.Equal(initialState, expiration.Before);
                Assert.Equal(
                    initialState with { Status = AppointmentStatus.Expired },
                    expiration.After);
                Assert.Equal(expiresAt, expiration.OccurredAt);
                Assert.Null(expiration.Reason);

                Assert.Equal(AppointmentStatus.Expired, appointment.Status);
                Assert.Equal(expiresAt, appointment.UpdatedAt);
                Assert.Equal(startsAt, appointment.StartsAt);
                Assert.Equal(startsAt.AddMinutes(45), appointment.EndsAt);
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentHistory_ShouldRecordWorkerExpirationOnlyOnce()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var startsAt = new DateTimeOffset(
            2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        var expiresAt = CreatedAt.AddMinutes(10);

        try
        {
            await SeedBookableAsync(data);

            AppointmentHistoryState initialState;

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = CreateAppointment(
                    data,
                    appointmentId,
                    startsAt);

                initialState = AppointmentHistoryState.Capture(appointment);

                context.Appointments.Add(appointment);

                context.AppointmentHistoryEntries.Add(
                    new AppointmentHistoryEntry(
                        Guid.NewGuid(),
                        data.TenantId,
                        appointmentId,
                        AppointmentHistoryType.Created,
                        CreatedAt,
                        null,
                        initialState));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var service = new AppointmentExpirationService(context);

                var count = await service.ExpireDueAsync(
                    expiresAt.AddTicks(-10));

                Assert.Equal(0, count);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var service = new AppointmentExpirationService(context);

                var count = await service.ExpireDueAsync(expiresAt);

                Assert.Equal(1, count);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var service = new AppointmentExpirationService(context);

                var count = await service.ExpireDueAsync(
                    expiresAt.AddMinutes(1));

                Assert.Equal(0, count);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                var entries = await context.AppointmentHistoryEntries
                    .AsNoTracking()
                    .Where(entry => entry.AppointmentId == appointmentId)
                    .ToListAsync();

                Assert.Equal(2, entries.Count);

                var creation = Assert.Single(
                    entries,
                    entry => entry.Type == AppointmentHistoryType.Created);

                var expiration = Assert.Single(
                    entries,
                    entry => entry.Type == AppointmentHistoryType.Expired);

                Assert.Equal(initialState, creation.After);
                Assert.NotNull(expiration.Before);
                Assert.Equal(initialState, expiration.Before);

                Assert.Equal(
                    initialState with { Status = AppointmentStatus.Expired },
                    expiration.After);

                Assert.Equal(
                    AppointmentHistoryState.Capture(appointment),
                    expiration.After);

                Assert.Equal(data.TenantId, expiration.TenantId);
                Assert.Equal(appointmentId, expiration.AppointmentId);
                Assert.Equal(expiresAt, expiration.OccurredAt);
                Assert.Equal(TimeSpan.Zero, expiration.OccurredAt.Offset);
                Assert.Equal(TimeSpan.Zero, expiration.After.StartsAt.Offset);
                Assert.Equal(TimeSpan.Zero, expiration.After.EndsAt.Offset);
                Assert.Null(expiration.Reason);

                Assert.Equal(AppointmentStatus.Expired, appointment.Status);
                Assert.Equal(expiresAt, appointment.UpdatedAt);
                Assert.Equal(expiresAt, appointment.ReservationExpiresAt);
                Assert.Equal(startsAt, appointment.StartsAt);
                Assert.Equal(startsAt.AddMinutes(45), appointment.EndsAt);
                Assert.Equal(65m, appointment.TotalPriceSnapshot);
                Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);
                Assert.Single(appointment.Items);

                Assert.False(
                    await context.IdempotencyRecords.AnyAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentHistory_ShouldRecordExpirationWhenReschedulingIsOverdue()
    {
        var data = new TestData();

        var originalStart = new DateTimeOffset(
            2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        var expiresAt = CreatedAt.AddMinutes(10);

        try
        {
            await SeedBookableAsync(data);

            Guid appointmentId;

            await using (var context = CreateContext(data.TenantId))
            {
                var creation = await CreateHandler(context).HandleAsync(
                    new CreateAppointmentCommand(
                        data.CustomerId,
                        data.LocationId,
                        data.ProfessionalId,
                        new[] { data.ServiceId },
                        originalStart,
                        Guid.NewGuid().ToString()));

                Assert.True(
                    creation.IsSuccess,
                    $"Criação falhou: {creation.Error}");

                Assert.NotNull(creation.Response);
                appointmentId = creation.Response.Id;
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                originalStart.AddHours(1),
                Guid.NewGuid().ToString());

            for (var attempt = 0; attempt < 2; attempt++)
            {
                await using var context = CreateContext(data.TenantId);

                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(
                            expiresAt.AddSeconds(attempt))));

                var result = await handler.HandleAsync(command);

                Assert.Equal(
                    RescheduleAppointmentError.ReservationExpired,
                    result.Error);

                Assert.Null(result.Response);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == appointmentId);

                var entries = await context.AppointmentHistoryEntries
                    .AsNoTracking()
                    .Where(entry => entry.AppointmentId == appointmentId)
                    .ToListAsync();

                Assert.Equal(2, entries.Count);

                var creation = Assert.Single(
                    entries,
                    entry => entry.Type == AppointmentHistoryType.Created);

                var expiration = Assert.Single(
                    entries,
                    entry => entry.Type == AppointmentHistoryType.Expired);

                Assert.NotNull(expiration.Before);
                Assert.Equal(creation.After, expiration.Before);
                Assert.Equal(
                    AppointmentStatus.Pending,
                    expiration.Before.Status);
                Assert.Equal(
                    AppointmentStatus.Expired,
                    expiration.After.Status);

                Assert.Equal(
                    AppointmentHistoryState.Capture(appointment),
                    expiration.After);

                Assert.Equal(expiresAt, expiration.OccurredAt);
                Assert.Null(expiration.Reason);

                Assert.Equal(originalStart, appointment.StartsAt);
                Assert.Equal(
                    originalStart.AddMinutes(45),
                    appointment.EndsAt);
                Assert.Equal(data.LocationId, appointment.LocationId);
                Assert.Equal(
                    data.ProfessionalId,
                    appointment.ProfessionalId);
                Assert.Equal(expiresAt, appointment.UpdatedAt);
                Assert.Equal(expiresAt, appointment.ReservationExpiresAt);

                Assert.DoesNotContain(
                    entries,
                    entry => entry.Type ==
                        AppointmentHistoryType.Rescheduled);

                Assert.False(
                    await context.IdempotencyRecords.AnyAsync(
                        record => record.Operation ==
                            "RescheduleAppointment"));
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentHistory_ShouldRecordReschedulingOnlyOnce()
    {
        var data = new TestData();

        var originalStart = new DateTimeOffset(
            2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        var newStart = originalStart.AddHours(1);
        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            Guid appointmentId;
            AppointmentHistoryState initialState;

            await using (var context = CreateContext(data.TenantId))
            {
                var creation = await CreateHandler(context).HandleAsync(
                    new CreateAppointmentCommand(
                        data.CustomerId,
                        data.LocationId,
                        data.ProfessionalId,
                        new[] { data.ServiceId },
                        originalStart,
                        Guid.NewGuid().ToString()));

                Assert.True(
                    creation.IsSuccess,
                    $"Criação falhou: {creation.Error}");

                Assert.NotNull(creation.Response);
                appointmentId = creation.Response.Id;

                var appointment = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == appointmentId);

                initialState = AppointmentHistoryState.Capture(appointment);
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                newStart,
                Guid.NewGuid().ToString());

            for (var attempt = 0; attempt < 3; attempt++)
            {
                await using var context = CreateContext(data.TenantId);

                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now.AddSeconds(attempt))));

                var request = attempt == 2
                    ? command with
                    {
                        IdempotencyKey = Guid.NewGuid().ToString()
                    }
                    : command;

                var result = await handler.HandleAsync(request);

                Assert.True(
                    result.IsSuccess,
                    $"Tentativa {attempt}: {result.Error}");

                Assert.NotNull(result.Response);
                Assert.Equal(appointmentId, result.Response.Id);
                Assert.Equal(newStart, result.Response.StartsAt);
                Assert.Equal(newStart.AddMinutes(45), result.Response.EndsAt);
                Assert.Equal(now, result.Response.UpdatedAt);
                Assert.Equal(
                    initialState.ReservationExpiresAt,
                    result.Response.ReservationExpiresAt);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                var entries = await context.AppointmentHistoryEntries
                    .AsNoTracking()
                    .Where(entry => entry.AppointmentId == appointmentId)
                    .ToListAsync();

                Assert.Equal(2, entries.Count);

                var creation = Assert.Single(
                    entries,
                    entry => entry.Type == AppointmentHistoryType.Created);

                var rescheduling = Assert.Single(
                    entries,
                    entry => entry.Type ==
                        AppointmentHistoryType.Rescheduled);

                Assert.Equal(initialState, creation.After);
                Assert.NotNull(rescheduling.Before);
                Assert.Equal(initialState, rescheduling.Before);

                Assert.Equal(
                    AppointmentHistoryState.Capture(appointment),
                    rescheduling.After);

                Assert.Equal(data.TenantId, rescheduling.TenantId);
                Assert.Equal(now, rescheduling.OccurredAt);
                Assert.Null(rescheduling.Reason);

                Assert.Equal(
                    originalStart,
                    rescheduling.Before.StartsAt);
                Assert.Equal(
                    originalStart.AddMinutes(45),
                    rescheduling.Before.EndsAt);
                Assert.Equal(newStart, rescheduling.After.StartsAt);
                Assert.Equal(
                    newStart.AddMinutes(45),
                    rescheduling.After.EndsAt);

                Assert.Equal(
                    rescheduling.Before.Status,
                    rescheduling.After.Status);
                Assert.Equal(
                    rescheduling.Before.ReservationExpiresAt,
                    rescheduling.After.ReservationExpiresAt);

                Assert.Equal(now, appointment.UpdatedAt);
                Assert.Equal(65m, appointment.TotalPriceSnapshot);
                Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);
                Assert.Single(appointment.Items);

                Assert.Equal(
                    2,
                    await context.IdempotencyRecords.CountAsync(
                        record => record.Operation ==
                            "RescheduleAppointment"));
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentHistory_ShouldRecordExpirationWhenCancellationIsOverdue()
    {
        var data = new TestData();

        var startsAt = new DateTimeOffset(
            2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        var expiresAt = CreatedAt.AddMinutes(10);

        try
        {
            await SeedBookableAsync(data);

            Guid appointmentId;

            await using (var context = CreateContext(data.TenantId))
            {
                var creation = await CreateHandler(context).HandleAsync(
                    new CreateAppointmentCommand(
                        data.CustomerId,
                        data.LocationId,
                        data.ProfessionalId,
                        new[] { data.ServiceId },
                        startsAt,
                        Guid.NewGuid().ToString()));

                Assert.True(
                    creation.IsSuccess,
                    $"Criação falhou: {creation.Error}");

                Assert.NotNull(creation.Response);
                appointmentId = creation.Response.Id;
            }

            var command = new CancelAppointmentCommand(
                appointmentId,
                AppointmentStatus.CancelledByClient,
                "Tentativa após o prazo",
                Guid.NewGuid().ToString());

            for (var attempt = 0; attempt < 2; attempt++)
            {
                await using var context = CreateContext(data.TenantId);

                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(
                            expiresAt.AddSeconds(attempt))));

                var result = await handler.HandleAsync(command);

                Assert.Equal(
                    CancelAppointmentError.ReservationExpired,
                    result.Error);

                Assert.Null(result.Response);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == appointmentId);

                var entries = await context.AppointmentHistoryEntries
                    .AsNoTracking()
                    .Where(entry => entry.AppointmentId == appointmentId)
                    .ToListAsync();

                Assert.Equal(2, entries.Count);

                var creation = Assert.Single(
                    entries,
                    entry => entry.Type == AppointmentHistoryType.Created);

                var expiration = Assert.Single(
                    entries,
                    entry => entry.Type == AppointmentHistoryType.Expired);

                Assert.NotNull(expiration.Before);
                Assert.Equal(creation.After, expiration.Before);

                Assert.Equal(
                    AppointmentStatus.Pending,
                    expiration.Before.Status);

                Assert.Equal(
                    AppointmentStatus.Expired,
                    expiration.After.Status);

                Assert.Equal(
                    AppointmentHistoryState.Capture(appointment),
                    expiration.After);

                Assert.Equal(expiresAt, expiration.OccurredAt);
                Assert.Null(expiration.Reason);

                Assert.Equal(
                    expiration.Before.StartsAt,
                    expiration.After.StartsAt);
                Assert.Equal(
                    expiration.Before.EndsAt,
                    expiration.After.EndsAt);
                Assert.Equal(
                    expiresAt,
                    expiration.After.ReservationExpiresAt);

                Assert.Equal(AppointmentStatus.Expired, appointment.Status);
                Assert.Equal(expiresAt, appointment.UpdatedAt);
                Assert.Null(appointment.CancellationReason);
                Assert.Null(appointment.CancelledAt);
                Assert.Equal(65m, appointment.TotalPriceSnapshot);
                Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);

                Assert.DoesNotContain(
                    entries,
                    entry => entry.Type == AppointmentHistoryType.Cancelled);

                Assert.False(
                    await context.IdempotencyRecords.AnyAsync(
                        record => record.Operation == "CancelAppointment"));
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentHistory_ShouldRecordCreationOnlyOnceOnReplay()
    {
        var data = new TestData();

        var startsAt = new DateTimeOffset(
            2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        var command = new CreateAppointmentCommand(
            data.CustomerId,
            data.LocationId,
            data.ProfessionalId,
            new[] { data.ServiceId },
            startsAt,
            Guid.NewGuid().ToString());

        try
        {
            await SeedBookableAsync(data);

            Guid appointmentId;

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = CreateHandler(context);

                var result = await handler.HandleAsync(command);

                Assert.True(
                    result.IsSuccess,
                    $"Criação falhou: {result.Error}");

                Assert.NotNull(result.Response);
                appointmentId = result.Response.Id;
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = CreateHandler(context);

                var replay = await handler.HandleAsync(command);

                Assert.True(
                    replay.IsSuccess,
                    $"Replay falhou: {replay.Error}");

                Assert.NotNull(replay.Response);
                Assert.Equal(appointmentId, replay.Response.Id);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == appointmentId);

                var entries = await context.AppointmentHistoryEntries
                    .AsNoTracking()
                    .Where(entry => entry.AppointmentId == appointmentId)
                    .ToListAsync();

                var history = Assert.Single(entries);

                Assert.Equal(data.TenantId, history.TenantId);
                Assert.Equal(appointmentId, history.AppointmentId);
                Assert.Equal(AppointmentHistoryType.Created, history.Type);
                Assert.Equal(appointment.CreatedAt, history.OccurredAt);
                Assert.Null(history.Before);
                Assert.Null(history.Reason);

                Assert.Equal(
                    AppointmentHistoryState.Capture(appointment),
                    history.After);

                Assert.Equal(1, await context.Appointments.CountAsync());
                Assert.Equal(1, await context.AppointmentItems.CountAsync());

                Assert.Equal(
                    1,
                    await context.IdempotencyRecords.CountAsync(
                        record => record.Operation == "CreateAppointment"));
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentHistory_ShouldRejectWritingAnotherTenantEntry()
    {
        var current = new TestData();
        var other = new TestData();

        var appointmentId = Guid.NewGuid();

        var startsAt = new DateTimeOffset(
            2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        try
        {
            await SeedBookableAsync(current);
            await SeedBookableAsync(other);

            AppointmentHistoryState state;

            await using (var context = CreateContext(other.TenantId))
            {
                var appointment = CreateAppointment(
                    other,
                    appointmentId,
                    startsAt);

                state = AppointmentHistoryState.Capture(appointment);

                context.Appointments.Add(appointment);
                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(current.TenantId))
            {
                context.AppointmentHistoryEntries.Add(
                    new AppointmentHistoryEntry(
                        Guid.NewGuid(),
                        other.TenantId,
                        appointmentId,
                        AppointmentHistoryType.Created,
                        CreatedAt,
                        null,
                        state));

                var exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => context.SaveChangesAsync());

                Assert.Equal(
                    "Cross-tenant data modification is not allowed.",
                    exception.Message);
            }

            await using (var context = CreateContext(other.TenantId))
            {
                Assert.False(
                    await context.AppointmentHistoryEntries.AnyAsync());
            }
        }
        finally
        {
            try
            {
                await CleanupAsync(current.TenantId);
            }
            finally
            {
                await CleanupAsync(other.TenantId);
            }
        }
    }

    [Fact]
    public async Task AppointmentHistory_ShouldRejectReferenceToAnotherTenantAppointment()
    {
        var current = new TestData();
        var other = new TestData();

        var appointmentId = Guid.NewGuid();

        var startsAt = new DateTimeOffset(
            2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        try
        {
            await SeedBookableAsync(current);
            await SeedBookableAsync(other);

            AppointmentHistoryState state;

            await using (var context = CreateContext(other.TenantId))
            {
                var appointment = CreateAppointment(
                    other,
                    appointmentId,
                    startsAt);

                state = AppointmentHistoryState.Capture(appointment);

                context.Appointments.Add(appointment);
                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(current.TenantId))
            {
                // O histórico pertence ao tenant atual, mas tenta
                // referenciar um agendamento existente em outro tenant.
                context.AppointmentHistoryEntries.Add(
                    new AppointmentHistoryEntry(
                        Guid.NewGuid(),
                        current.TenantId,
                        appointmentId,
                        AppointmentHistoryType.Created,
                        CreatedAt,
                        null,
                        state));

                var exception =
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync());

                var postgresException =
                    Assert.IsType<Npgsql.PostgresException>(
                        exception.InnerException);

                Assert.Equal(
                    Npgsql.PostgresErrorCodes.ForeignKeyViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    "appointment_history_entries",
                    postgresException.TableName);
            }

            await using (var context = CreateContext(current.TenantId))
            {
                Assert.False(
                    await context.AppointmentHistoryEntries.AnyAsync());
            }

            await using (var context = CreateContext(other.TenantId))
            {
                Assert.True(
                    await context.Appointments.AnyAsync(
                        appointment => appointment.Id == appointmentId));

                Assert.False(
                    await context.AppointmentHistoryEntries.AnyAsync());
            }
        }
        finally
        {
            try
            {
                await CleanupAsync(current.TenantId);
            }
            finally
            {
                await CleanupAsync(other.TenantId);
            }
        }
    }

    [Fact]
    public async Task AppointmentHistory_ShouldIsolateReadsBetweenTenants()
    {
        var current = new TestData();
        var other = new TestData();

        var appointmentId = Guid.NewGuid();
        var historyId = Guid.NewGuid();

        var startsAt = new DateTimeOffset(
            2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        try
        {
            await SeedBookableAsync(current);
            await SeedBookableAsync(other);

            await using (var context = CreateContext(other.TenantId))
            {
                var appointment = CreateAppointment(
                    other,
                    appointmentId,
                    startsAt);

                context.Appointments.Add(appointment);

                context.AppointmentHistoryEntries.Add(
                    new AppointmentHistoryEntry(
                        historyId,
                        other.TenantId,
                        appointmentId,
                        AppointmentHistoryType.Created,
                        CreatedAt,
                        null,
                        AppointmentHistoryState.Capture(appointment)));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(current.TenantId))
            {
                Assert.False(
                    await context.AppointmentHistoryEntries.AnyAsync());

                Assert.Null(
                    await context.AppointmentHistoryEntries
                        .AsNoTracking()
                        .SingleOrDefaultAsync(entry => entry.Id == historyId));

                Assert.Equal(
                    0,
                    await context.AppointmentHistoryEntries.CountAsync(
                        entry => entry.AppointmentId == appointmentId));
            }

            await using (var context = CreateContext(other.TenantId))
            {
                var entry = await context.AppointmentHistoryEntries
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == historyId);

                Assert.Equal(other.TenantId, entry.TenantId);
                Assert.Equal(appointmentId, entry.AppointmentId);
                Assert.Equal(AppointmentHistoryType.Created, entry.Type);
                Assert.Null(entry.Before);
                Assert.Equal(startsAt, entry.After.StartsAt);
            }
        }
        finally
        {
            try
            {
                await CleanupAsync(current.TenantId);
            }
            finally
            {
                await CleanupAsync(other.TenantId);
            }
        }
    }

    [Fact]
    public async Task AppointmentHistory_ShouldPersistCreationAndReschedulingStates()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var creationHistoryId = Guid.NewGuid();
        var reschedulingHistoryId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        var rescheduledAt = CreatedAt.AddMinutes(5);
        var newStart = originalStart.AddHours(1);

        try
        {
            await SeedBookableAsync(data);

            AppointmentHistoryState initialState;
            AppointmentHistoryState rescheduledState;

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = CreateAppointment(
                    data,
                    appointmentId,
                    originalStart);

                initialState = AppointmentHistoryState.Capture(appointment);

                context.Appointments.Add(appointment);

                context.AppointmentHistoryEntries.Add(
                    new AppointmentHistoryEntry(
                        creationHistoryId,
                        data.TenantId,
                        appointmentId,
                        AppointmentHistoryType.Created,
                        CreatedAt,
                        null,
                        initialState));

                await context.SaveChangesAsync();

                Assert.True(
                    appointment.Reschedule(
                        data.LocationId,
                        data.ProfessionalId,
                        newStart,
                        rescheduledAt));

                rescheduledState =
                    AppointmentHistoryState.Capture(appointment);

                context.AppointmentHistoryEntries.Add(
                    new AppointmentHistoryEntry(
                        reschedulingHistoryId,
                        data.TenantId,
                        appointmentId,
                        AppointmentHistoryType.Rescheduled,
                        rescheduledAt,
                        initialState,
                        rescheduledState));

                await context.SaveChangesAsync();
            }

            // Um novo contexto garante leitura real do PostgreSQL,
            // incluindo a desserialização dos estados em jsonb.
            await using (var context = CreateContext(data.TenantId))
            {
                var entries = await context.AppointmentHistoryEntries
                    .AsNoTracking()
                    .Where(entry => entry.AppointmentId == appointmentId)
                    .OrderBy(entry => entry.OccurredAt)
                    .ThenBy(entry => entry.Id)
                    .ToListAsync();

                Assert.Equal(2, entries.Count);

                var creation = entries[0];

                Assert.Equal(creationHistoryId, creation.Id);
                Assert.Equal(data.TenantId, creation.TenantId);
                Assert.Equal(appointmentId, creation.AppointmentId);
                Assert.Equal(AppointmentHistoryType.Created, creation.Type);
                Assert.Equal(CreatedAt, creation.OccurredAt);
                Assert.Null(creation.Before);
                Assert.Equal(initialState, creation.After);
                Assert.Null(creation.Reason);

                var rescheduling = entries[1];

                Assert.Equal(reschedulingHistoryId, rescheduling.Id);
                Assert.Equal(
                    AppointmentHistoryType.Rescheduled,
                    rescheduling.Type);
                Assert.Equal(rescheduledAt, rescheduling.OccurredAt);
                Assert.Equal(initialState, rescheduling.Before);
                Assert.Equal(rescheduledState, rescheduling.After);
                Assert.Null(rescheduling.Reason);

                Assert.Equal(originalStart, creation.After.StartsAt);
                Assert.NotNull(rescheduling.Before);
                Assert.Equal(originalStart, rescheduling.Before.StartsAt);
                Assert.Equal(newStart, rescheduling.After.StartsAt);
                Assert.Equal(
                    newStart.AddMinutes(45),
                    rescheduling.After.EndsAt);

                var persisted = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(newStart, persisted.StartsAt);
                Assert.Equal(
                    initialState.ReservationExpiresAt,
                    persisted.ReservationExpiresAt);
                Assert.Equal(65m, persisted.TotalPriceSnapshot);
                Assert.Equal(45, persisted.TotalDurationMinutesSnapshot);
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentReschedulingService_WithForeignAppointment_ShouldReturnNotFound()
    {
        var current = new TestData();
        var other = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        try
        {
            await SeedBookableAsync(current);
            await SeedBookableAsync(other);

            await using (var context = CreateContext(other.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        other,
                        appointmentId,
                        originalStart));

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                current.LocationId,
                current.ProfessionalId,
                originalStart.AddHours(1),
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(current.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(
                            CreatedAt.AddMinutes(5))));

                var result = await handler.HandleAsync(command);

                Assert.Equal(
                    RescheduleAppointmentError.AppointmentNotFound,
                    result.Error);
                Assert.Null(result.Response);

                Assert.Equal(
                    0,
                    await context.Appointments.CountAsync());
                Assert.Equal(
                    0,
                    await context.IdempotencyRecords.CountAsync());
            }

            await using (var context = CreateContext(other.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(other.LocationId, persisted.LocationId);
                Assert.Equal(
                    other.ProfessionalId,
                    persisted.ProfessionalId);
                Assert.Equal(originalStart, persisted.StartsAt);
                Assert.Equal(
                    originalStart.AddMinutes(45),
                    persisted.EndsAt);
                Assert.Equal(AppointmentStatus.Pending, persisted.Status);
                Assert.Equal(CreatedAt, persisted.UpdatedAt);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    persisted.ReservationExpiresAt);
                Assert.Single(persisted.Items);

                Assert.Equal(
                    0,
                    await context.IdempotencyRecords.CountAsync());
            }
        }
        finally
        {
            try
            {
                await CleanupAsync(current.TenantId);
            }
            finally
            {
                await CleanupAsync(other.TenantId);
            }
        }
    }

    [Fact]
    public async Task AppointmentReschedulingService_WithConcurrentSameKey_ShouldReplayWinner()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        originalStart));

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                originalStart.AddHours(1),
                Guid.NewGuid().ToString());

            await using var firstContext = CreateContext(data.TenantId);
            await using var secondContext = CreateContext(data.TenantId);

            var firstHandler = new RescheduleAppointmentHandler(
                new AppointmentReschedulingService(
                    firstContext,
                    new FixedTimeProvider(now)));

            var secondHandler = new RescheduleAppointmentHandler(
                new AppointmentReschedulingService(
                    secondContext,
                    new FixedTimeProvider(now.AddSeconds(1))));

            var start = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(30));

            async Task<RescheduleAppointmentResult> ExecuteAsync(
                RescheduleAppointmentHandler handler)
            {
                await start.Task.WaitAsync(timeout.Token);

                return await handler.HandleAsync(
                    command,
                    timeout.Token);
            }

            var firstTask = ExecuteAsync(firstHandler);
            var secondTask = ExecuteAsync(secondHandler);

            start.SetResult(true);

            var results = await Task.WhenAll(
                firstTask,
                secondTask);

            Assert.All(
                results,
                result => Assert.True(
                    result.IsSuccess,
                    $"Erro de reagendamento: {result.Error}"));

            var firstResponse = Assert.IsType<AppointmentResponse>(
                results[0].Response);

            var secondResponse = Assert.IsType<AppointmentResponse>(
                results[1].Response);

            Assert.Equal(
                firstResponse with
                {
                    Items = secondResponse.Items
                },
                secondResponse);

            Assert.Equal(
                firstResponse.Items.ToArray(),
                secondResponse.Items.ToArray());

            Assert.Equal(command.StartsAt, firstResponse.StartsAt);

            Assert.Contains(
                firstResponse.UpdatedAt,
                new[] { now, now.AddSeconds(1) });

            await using var readContext = CreateContext(data.TenantId);

            var persisted = await readContext.Appointments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == appointmentId);

            Assert.Equal(command.StartsAt, persisted.StartsAt);
            Assert.Equal(
                command.StartsAt.AddMinutes(45),
                persisted.EndsAt);
            Assert.Equal(
                firstResponse.UpdatedAt,
                persisted.UpdatedAt);
            Assert.Equal(AppointmentStatus.Pending, persisted.Status);
            Assert.Equal(
                CreatedAt.AddMinutes(10),
                persisted.ReservationExpiresAt);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal("RescheduleAppointment", record.Operation);
            Assert.Equal(command.IdempotencyKey, record.Key);
            Assert.Equal(firstResponse.UpdatedAt, record.CreatedAt);

            var storedResponse =
                System.Text.Json.JsonSerializer
                    .Deserialize<AppointmentResponse>(
                        record.ResponseJson);

            Assert.NotNull(storedResponse);
            Assert.Equal(firstResponse.StartsAt, storedResponse.StartsAt);
            Assert.Equal(firstResponse.UpdatedAt, storedResponse.UpdatedAt);
            Assert.Equal(
                firstResponse.Items.ToArray(),
                storedResponse.Items.ToArray());

            Assert.Equal(
                1,
                await readContext.Appointments.CountAsync());
            Assert.Equal(
                1,
                await readContext.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentReschedulingService_WithConcurrentDifferentDestinations_ShouldPersistOnlyOne()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        originalStart));

                await context.SaveChangesAsync();
            }

            var firstCommand = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                originalStart.AddHours(1),
                Guid.NewGuid().ToString());

            var secondCommand = firstCommand with
            {
                StartsAt = originalStart.AddHours(2),
                IdempotencyKey = Guid.NewGuid().ToString()
            };

            var gate = new ConcurrentSaveGate();

            await using var firstContext = CreateContext(
                data.TenantId,
                gate);

            await using var secondContext = CreateContext(
                data.TenantId,
                gate);

            var firstHandler = new RescheduleAppointmentHandler(
                new AppointmentReschedulingService(
                    firstContext,
                    new FixedTimeProvider(now)));

            var secondHandler = new RescheduleAppointmentHandler(
                new AppointmentReschedulingService(
                    secondContext,
                    new FixedTimeProvider(now)));

            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(30));

            var results = await Task.WhenAll(
                firstHandler.HandleAsync(
                    firstCommand,
                    timeout.Token),
                secondHandler.HandleAsync(
                    secondCommand,
                    timeout.Token));

            Assert.Equal(2, gate.Arrivals);

            var winner = Assert.Single(
                results,
                result => result.IsSuccess);

            var loser = Assert.Single(
                results,
                result => !result.IsSuccess);

            Assert.Equal(
                RescheduleAppointmentError.ConcurrencyConflict,
                loser.Error);
            Assert.Null(loser.Response);

            var winningCommand = results[0].IsSuccess
                ? firstCommand
                : secondCommand;

            var losingContext = results[0].IsSuccess
                ? secondContext
                : firstContext;

            Assert.Empty(losingContext.ChangeTracker.Entries());

            var response = Assert.IsType<AppointmentResponse>(
                winner.Response);

            Assert.Equal(winningCommand.StartsAt, response.StartsAt);

            await using var readContext = CreateContext(data.TenantId);

            var persisted = await readContext.Appointments
                .AsNoTracking()
                .Include(entity => entity.Items)
                .SingleAsync(entity => entity.Id == appointmentId);

            Assert.Equal(
                winningCommand.StartsAt,
                persisted.StartsAt);
            Assert.Equal(
                winningCommand.StartsAt.AddMinutes(45),
                persisted.EndsAt);
            Assert.Equal(AppointmentStatus.Pending, persisted.Status);
            Assert.Equal(now, persisted.UpdatedAt);
            Assert.Equal(
                CreatedAt.AddMinutes(10),
                persisted.ReservationExpiresAt);
            Assert.Equal(65m, persisted.TotalPriceSnapshot);
            Assert.Equal(45, persisted.TotalDurationMinutesSnapshot);
            Assert.Single(persisted.Items);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal("RescheduleAppointment", record.Operation);
            Assert.Equal(winningCommand.IdempotencyKey, record.Key);

            var storedResponse =
                System.Text.Json.JsonSerializer
                    .Deserialize<AppointmentResponse>(
                        record.ResponseJson);

            Assert.NotNull(storedResponse);
            Assert.Equal(response.StartsAt, storedResponse.StartsAt);
            Assert.Equal(response.EndsAt, storedResponse.EndsAt);

            Assert.Equal(
                1,
                await readContext.Appointments.CountAsync());
            Assert.Equal(
                1,
                await readContext.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentReschedulingService_ShouldChangeLocationAndProfessional()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var destinationLocationId = Guid.NewGuid();
        var destinationProfessionalId = Guid.NewGuid();

        var startsAt = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Locations.Add(
                    new Location(
                        destinationLocationId,
                        data.TenantId,
                        data.BusinessId,
                        "Unidade de destino",
                        "BR",
                        "America/Sao_Paulo",
                        CreatedAt));

                context.Professionals.Add(
                    new Professional(
                        destinationProfessionalId,
                        data.TenantId,
                        data.BusinessId,
                        "Profissional de destino",
                        CreatedAt));

                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        startsAt));

                await context.SaveChangesAsync();

                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        data.TenantId,
                        destinationProfessionalId,
                        destinationLocationId,
                        CreatedAt));

                context.ProfessionalServices.Add(
                    new ProfessionalService(
                        data.TenantId,
                        destinationProfessionalId,
                        data.ServiceId,
                        CreatedAt));

                context.LocationServices.Add(
                    new LocationService(
                        data.TenantId,
                        destinationLocationId,
                        data.ServiceId,
                        CreatedAt));

                await context.SaveChangesAsync();

                context.AvailabilityRules.Add(
                    new AvailabilityRule(
                        Guid.NewGuid(),
                        data.TenantId,
                        destinationLocationId,
                        destinationProfessionalId,
                        DayOfWeek.Monday,
                        new TimeOnly(9, 0),
                        new TimeOnly(18, 0),
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                destinationLocationId,
                destinationProfessionalId,
                startsAt,
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(command);

                Assert.True(
                    result.IsSuccess,
                    $"Erro de reagendamento: {result.Error}");

                var response = Assert.IsType<AppointmentResponse>(
                    result.Response);

                Assert.Equal(appointmentId, response.Id);
                Assert.Equal(
                    destinationLocationId,
                    response.LocationId);
                Assert.Equal(
                    destinationProfessionalId,
                    response.ProfessionalId);
                Assert.Equal(data.BusinessId, response.BusinessId);
                Assert.Equal(data.CustomerId, response.CustomerId);
                Assert.Equal(startsAt, response.StartsAt);
                Assert.Equal(startsAt.AddMinutes(45), response.EndsAt);
                Assert.Equal(65m, response.TotalPrice);
                Assert.Equal(45, response.TotalDurationMinutes);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(
                    destinationLocationId,
                    persisted.LocationId);
                Assert.Equal(
                    destinationProfessionalId,
                    persisted.ProfessionalId);
                Assert.Equal(AppointmentStatus.Pending, persisted.Status);
                Assert.Equal(CreatedAt, persisted.CreatedAt);
                Assert.Equal(now, persisted.UpdatedAt);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    persisted.ReservationExpiresAt);

                var item = Assert.Single(persisted.Items);

                Assert.Equal(data.ServiceId, item.ServiceId);
                Assert.Equal("Corte", item.ServiceNameSnapshot);
                Assert.Equal(65m, item.PriceSnapshot);
                Assert.Equal(45, item.DurationMinutesSnapshot);

                Assert.Equal(
                    1,
                    await context.IdempotencyRecords.CountAsync());
                Assert.Equal(
                    1,
                    await context.Appointments.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentReschedulingService_ShouldReplayWithEquivalentOffsetAfterDeadline()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var newStart = originalStart.AddHours(1);
        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        originalStart));

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                newStart,
                Guid.NewGuid().ToString());

            AppointmentResponse originalResponse;

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(command);

                Assert.True(result.IsSuccess);

                originalResponse = Assert.IsType<AppointmentResponse>(
                    result.Response);
            }

            // Simula a expiração após o reagendamento.
            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.True(
                    appointment.Expire(CreatedAt.AddMinutes(10)));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(newStart.AddHours(1))));

                var replay = await handler.HandleAsync(
                    command with
                    {
                        StartsAt = newStart.ToOffset(
                            TimeSpan.FromHours(-3))
                    });

                Assert.True(replay.IsSuccess);

                var response = Assert.IsType<AppointmentResponse>(
                    replay.Response);

                // Compara os itens por conteúdo, pois o replay
                // desserializa uma nova coleção.
                Assert.Equal(
                    originalResponse with
                    {
                        Items = response.Items
                    },
                    response);

                Assert.Equal(
                    originalResponse.Items.ToArray(),
                    response.Items.ToArray());

                Assert.Equal("PENDING", response.Status);
                Assert.Equal(now, response.UpdatedAt);
                Assert.Equal(newStart, response.StartsAt);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == appointmentId);

                // Replay não reativa nem altera o estado atual.
                Assert.Equal(
                    AppointmentStatus.Expired,
                    persisted.Status);
                Assert.Equal(newStart, persisted.StartsAt);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    persisted.UpdatedAt);

                var record = await context.IdempotencyRecords
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal("RescheduleAppointment", record.Operation);
                Assert.Equal(command.IdempotencyKey, record.Key);
                Assert.Equal(now, record.CreatedAt);
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentReschedulingService_WithOccupiedDestination_ShouldPreserveOriginalBooking()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var occupyingId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var occupiedStart = originalStart.AddHours(1);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        originalStart));

                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        occupyingId,
                        occupiedStart));

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                occupiedStart,
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(
                            CreatedAt.AddMinutes(5))));

                var result = await handler.HandleAsync(command);

                Assert.False(result.IsSuccess);
                Assert.Equal(
                    RescheduleAppointmentError.SlotUnavailable,
                    result.Error);
                Assert.Null(result.Response);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var original = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(originalStart, original.StartsAt);
                Assert.Equal(
                    originalStart.AddMinutes(45),
                    original.EndsAt);
                Assert.Equal(data.LocationId, original.LocationId);
                Assert.Equal(
                    data.ProfessionalId,
                    original.ProfessionalId);
                Assert.Equal(AppointmentStatus.Pending, original.Status);
                Assert.True(original.OccupiesSchedule);
                Assert.Equal(CreatedAt, original.UpdatedAt);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    original.ReservationExpiresAt);
                Assert.Equal(65m, original.TotalPriceSnapshot);
                Assert.Equal(45, original.TotalDurationMinutesSnapshot);
                Assert.Single(original.Items);

                var occupying = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == occupyingId);

                Assert.Equal(occupiedStart, occupying.StartsAt);
                Assert.Equal(
                    AppointmentStatus.Pending,
                    occupying.Status);

                Assert.Equal(
                    0,
                    await context.IdempotencyRecords.CountAsync());
                Assert.Equal(
                    2,
                    await context.Appointments.CountAsync());
                Assert.Equal(
                    2,
                    await context.AppointmentItems.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentReschedulingService_ShouldIgnoreOwnIntervalAndPreserveSnapshots()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var newStart = originalStart.AddMinutes(15);
        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            Guid originalItemId;

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = CreateAppointment(
                    data,
                    appointmentId,
                    originalStart);

                originalItemId = Assert.Single(appointment.Items).Id;

                context.Appointments.Add(appointment);

                await context.SaveChangesAsync();
            }

            // O catálogo muda, mas os valores do agendamento permanecem.
            await using (var context = CreateContext(data.TenantId))
            {
                var service = await context.Services
                    .SingleAsync(entity => entity.Id == data.ServiceId);

                service.Update(
                    "Corte atualizado",
                    90m,
                    60,
                    true,
                    now);

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                newStart,
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(command);

                Assert.True(
                    result.IsSuccess,
                    $"Erro de reagendamento: {result.Error}");

                var response = Assert.IsType<AppointmentResponse>(
                    result.Response);

                Assert.Equal(appointmentId, response.Id);
                Assert.Equal(newStart, response.StartsAt);
                Assert.Equal(newStart.AddMinutes(45), response.EndsAt);
                Assert.Equal("PENDING", response.Status);
                Assert.Equal(65m, response.TotalPrice);
                Assert.Equal(45, response.TotalDurationMinutes);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    response.ReservationExpiresAt);

                var item = Assert.Single(response.Items);

                Assert.Equal(data.ServiceId, item.ServiceId);
                Assert.Equal("Corte", item.Name);
                Assert.Equal(65m, item.Price);
                Assert.Equal(45, item.DurationMinutes);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(data.LocationId, persisted.LocationId);
                Assert.Equal(data.ProfessionalId, persisted.ProfessionalId);
                Assert.Equal(data.CustomerId, persisted.CustomerId);
                Assert.Equal(data.BusinessId, persisted.BusinessId);

                Assert.Equal(newStart, persisted.StartsAt);
                Assert.Equal(newStart.AddMinutes(45), persisted.EndsAt);
                Assert.Equal(now, persisted.UpdatedAt);
                Assert.Equal(CreatedAt, persisted.CreatedAt);
                Assert.Equal(65m, persisted.TotalPriceSnapshot);
                Assert.Equal(45, persisted.TotalDurationMinutesSnapshot);

                var item = Assert.Single(persisted.Items);

                Assert.Equal(originalItemId, item.Id);
                Assert.Equal("Corte", item.ServiceNameSnapshot);
                Assert.Equal(65m, item.PriceSnapshot);
                Assert.Equal(45, item.DurationMinutesSnapshot);

                var record = await context.IdempotencyRecords
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal("RescheduleAppointment", record.Operation);
                Assert.Equal(command.IdempotencyKey, record.Key);

                var storedResponse =
                    System.Text.Json.JsonSerializer
                        .Deserialize<AppointmentResponse>(
                            record.ResponseJson);

                Assert.NotNull(storedResponse);
                Assert.Equal(appointmentId, storedResponse.Id);
                Assert.Equal(newStart, storedResponse.StartsAt);
                Assert.Equal(65m, storedResponse.TotalPrice);
                Assert.Equal(45, storedResponse.TotalDurationMinutes);

                Assert.Equal(1, await context.Appointments.CountAsync());
                Assert.Equal(1, await context.AppointmentItems.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentCancellationService_WhenExpirationWins_ShouldNotOverwriteExpiredState()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var expiresAt = CreatedAt.AddMinutes(10);
        var expiredCount = 0;

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            var interceptor = new BeforeCancellationSaveInterceptor(
                async () =>
                {
                    await using var expirationContext =
                        CreateContext(data.TenantId);

                    var expirationService =
                        new AppointmentExpirationService(
                            expirationContext);

                    expiredCount = await expirationService.ExpireDueAsync(
                        expiresAt);
                });

            var command = new CancelAppointmentCommand(
                appointmentId,
                AppointmentStatus.CancelledByClient,
                "Cliente solicitou",
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(
                data.TenantId,
                interceptor))
            {
                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(
                            expiresAt.AddSeconds(-1))));

                var result = await handler.HandleAsync(command);

                Assert.Equal(1, expiredCount);
                Assert.Equal(
                    CancelAppointmentError.ConcurrencyConflict,
                    result.Error);
                Assert.Null(result.Response);
                Assert.Empty(context.ChangeTracker.Entries());
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(
                    AppointmentStatus.Expired,
                    appointment.Status);
                Assert.Equal(expiresAt, appointment.UpdatedAt);
                Assert.Null(appointment.CancelledAt);
                Assert.Null(appointment.CancellationReason);
                Assert.False(appointment.OccupiesSchedule);
                Assert.Equal(65m, appointment.TotalPriceSnapshot);
                Assert.Single(appointment.Items);

                Assert.Equal(
                    0,
                    await context.IdempotencyRecords.CountAsync());
            }

            // Uma nova tentativa observa o estado vencedor.
            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(expiresAt.AddSeconds(1))));

                var retry = await handler.HandleAsync(command);

                Assert.Equal(
                    CancelAppointmentError.ReservationExpired,
                    retry.Error);
                Assert.Null(retry.Response);
                Assert.Equal(
                    0,
                    await context.IdempotencyRecords.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentCancellationService_WithConcurrentDifferentKeys_ShouldPersistOnlyOne()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            var firstCommand = new CancelAppointmentCommand(
                appointmentId,
                AppointmentStatus.CancelledByClient,
                "Cliente solicitou",
                Guid.NewGuid().ToString());

            var secondCommand = new CancelAppointmentCommand(
                appointmentId,
                AppointmentStatus.CancelledByBusiness,
                "Estabelecimento solicitou",
                Guid.NewGuid().ToString());

            var gate = new ConcurrentSaveGate();

            await using var firstContext = CreateContext(
                data.TenantId,
                gate);

            await using var secondContext = CreateContext(
                data.TenantId,
                gate);

            var firstHandler = new CancelAppointmentHandler(
                new AppointmentCancellationService(
                    firstContext,
                    new FixedTimeProvider(now)));

            var secondHandler = new CancelAppointmentHandler(
                new AppointmentCancellationService(
                    secondContext,
                    new FixedTimeProvider(now)));

            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(30));

            var results = await Task.WhenAll(
                firstHandler.HandleAsync(
                    firstCommand,
                    timeout.Token),
                secondHandler.HandleAsync(
                    secondCommand,
                    timeout.Token));

            Assert.Equal(2, gate.Arrivals);

            var winner = Assert.Single(
                results,
                result => result.IsSuccess);

            var loser = Assert.Single(
                results,
                result => !result.IsSuccess);

            Assert.Equal(
                CancelAppointmentError.ConcurrencyConflict,
                loser.Error);
            Assert.Null(loser.Response);

            var response = Assert.IsType<CancelAppointmentResponse>(
                winner.Response);

            var firstWon = results[0].IsSuccess;
            var winningCommand = firstWon
                ? firstCommand
                : secondCommand;

            var losingContext = firstWon
                ? secondContext
                : firstContext;

            // A tentativa revertida não deve deixar alterações pendentes.
            Assert.Empty(losingContext.ChangeTracker.Entries());

            await using var readContext = CreateContext(data.TenantId);

            var persisted = await readContext.Appointments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == appointmentId);

            Assert.Equal(
                winningCommand.CancellationStatus,
                persisted.Status);
            Assert.Equal(
                winningCommand.Reason,
                persisted.CancellationReason);
            Assert.Equal(response.CancelledAt, persisted.CancelledAt);
            Assert.Equal(now, persisted.UpdatedAt);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal("CancelAppointment", record.Operation);
            Assert.Equal(winningCommand.IdempotencyKey, record.Key);

            var storedResponse =
                System.Text.Json.JsonSerializer
                    .Deserialize<CancelAppointmentResponse>(
                        record.ResponseJson);

            Assert.Equal(response, storedResponse);
            Assert.Equal(1, await readContext.Appointments.CountAsync());
            Assert.Equal(1, await readContext.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentCancellationService_WithConcurrentSameKey_ShouldReplayWinner()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            var command = new CancelAppointmentCommand(
                appointmentId,
                AppointmentStatus.CancelledByClient,
                "Cliente solicitou",
                Guid.NewGuid().ToString());

            await using var firstContext = CreateContext(data.TenantId);
            await using var secondContext = CreateContext(data.TenantId);

            var firstHandler = new CancelAppointmentHandler(
                new AppointmentCancellationService(
                    firstContext,
                    new FixedTimeProvider(now)));

            var secondHandler = new CancelAppointmentHandler(
                new AppointmentCancellationService(
                    secondContext,
                    new FixedTimeProvider(now.AddSeconds(1))));

            var start = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(30));

            async Task<CancelAppointmentResult> ExecuteAsync(
                CancelAppointmentHandler handler)
            {
                await start.Task.WaitAsync(timeout.Token);

                return await handler.HandleAsync(
                    command,
                    timeout.Token);
            }

            var firstTask = ExecuteAsync(firstHandler);
            var secondTask = ExecuteAsync(secondHandler);

            start.SetResult(true);

            var results = await Task.WhenAll(
                firstTask,
                secondTask);

            Assert.All(results, result => Assert.True(result.IsSuccess));

            var firstResponse = Assert.IsType<CancelAppointmentResponse>(
                results[0].Response);

            var secondResponse = Assert.IsType<CancelAppointmentResponse>(
                results[1].Response);

            Assert.Equal(firstResponse, secondResponse);
            Assert.Equal(appointmentId, firstResponse.Id);

            Assert.Contains(
                firstResponse.CancelledAt,
                new[] { now, now.AddSeconds(1) });

            await using var readContext = CreateContext(data.TenantId);

            var appointment = await readContext.Appointments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == appointmentId);

            Assert.Equal(
                AppointmentStatus.CancelledByClient,
                appointment.Status);
            Assert.Equal(
                firstResponse.CancelledAt,
                appointment.CancelledAt);
            Assert.Equal(
                firstResponse.UpdatedAt,
                appointment.UpdatedAt);
            Assert.Equal("Cliente solicitou", appointment.CancellationReason);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal("CancelAppointment", record.Operation);
            Assert.Equal(command.IdempotencyKey, record.Key);

            var storedResponse =
                System.Text.Json.JsonSerializer
                    .Deserialize<CancelAppointmentResponse>(
                        record.ResponseJson);

            Assert.Equal(firstResponse, storedResponse);
            Assert.Equal(1, await readContext.Appointments.CountAsync());
            Assert.Equal(1, await readContext.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentCancellationService_ShouldIsolateAppointmentAndKeyByTenant()
    {
        var current = new TestData();
        var other = new TestData();

        var currentId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();
        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedAsync(current);
            await SeedAsync(other);

            await using (var context = CreateContext(current.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        current,
                        currentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(other.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        other,
                        otherId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            var currentCommand = new CancelAppointmentCommand(
                currentId,
                AppointmentStatus.CancelledByClient,
                "Cliente solicitou",
                key);

            await using (var context = CreateContext(current.TenantId))
            {
                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(now)));

                Assert.True(
                    (await handler.HandleAsync(currentCommand)).IsSuccess);
            }

            // A chave existente no primeiro tenant não deve gerar replay
            // nem conflito ao consultar um agendamento invisível no segundo.
            await using (var context = CreateContext(other.TenantId))
            {
                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(currentCommand);

                Assert.Equal(
                    CancelAppointmentError.AppointmentNotFound,
                    result.Error);
                Assert.Null(result.Response);
                Assert.Equal(
                    0,
                    await context.IdempotencyRecords.CountAsync());

                var untouched = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal(otherId, untouched.Id);
                Assert.Equal(AppointmentStatus.Pending, untouched.Status);
                Assert.Null(untouched.CancelledAt);
            }

            // A mesma chave pode ser usada pelo outro tenant
            // para cancelar seu próprio agendamento.
            await using (var context = CreateContext(other.TenantId))
            {
                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(
                    currentCommand with
                    {
                        AppointmentId = otherId
                    });

                Assert.True(result.IsSuccess);
                Assert.NotNull(result.Response);
                Assert.Equal(otherId, result.Response.Id);
            }

            foreach (var data in new[] { current, other })
            {
                await using var context = CreateContext(data.TenantId);

                var appointment = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal(
                    AppointmentStatus.CancelledByClient,
                    appointment.Status);
                Assert.Equal(now, appointment.CancelledAt);

                var record = await context.IdempotencyRecords
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal(data.TenantId, record.TenantId);
                Assert.Equal("CancelAppointment", record.Operation);
                Assert.Equal(key, record.Key);
            }
        }
        finally
        {
            try
            {
                await CleanupAsync(current.TenantId);
            }
            finally
            {
                await CleanupAsync(other.TenantId);
            }
        }
    }

    [Fact]
    public async Task AppointmentCancellationService_ShouldPersistAndReplayOriginalResponse()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();
        var cancelledAt = CreatedAt.AddMinutes(5);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            var command = new CancelAppointmentCommand(
                appointmentId,
                AppointmentStatus.CancelledByClient,
                "  Cliente solicitou  ",
                key);

            CancelAppointmentResponse original;

            await using (var context = CreateContext(data.TenantId))
            {
                var service = new AppointmentCancellationService(
                    context,
                    new FixedTimeProvider(cancelledAt));

                var handler = new CancelAppointmentHandler(service);

                var result = await handler.HandleAsync(command);

                Assert.True(result.IsSuccess);

                original = Assert.IsType<CancelAppointmentResponse>(
                    result.Response);

                Assert.Equal(appointmentId, original.Id);
                Assert.Equal("CANCELLED_BY_CLIENT", original.Status);
                Assert.Equal(
                    "Cliente solicitou",
                    original.CancellationReason);
                Assert.Equal(cancelledAt, original.CancelledAt);
                Assert.Equal(cancelledAt, original.UpdatedAt);
            }

            // O replay ocorre depois do prazo original da reserva.
            await using (var context = CreateContext(data.TenantId))
            {
                var service = new AppointmentCancellationService(
                    context,
                    new FixedTimeProvider(CreatedAt.AddHours(3)));

                var handler = new CancelAppointmentHandler(service);

                var replay = await handler.HandleAsync(
                    command with
                    {
                        Reason = "Cliente solicitou"
                    });

                Assert.True(replay.IsSuccess);
                Assert.Equal(original, replay.Response);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(
                    AppointmentStatus.CancelledByClient,
                    appointment.Status);
                Assert.False(appointment.OccupiesSchedule);
                Assert.Equal(
                    "Cliente solicitou",
                    appointment.CancellationReason);
                Assert.Equal(cancelledAt, appointment.CancelledAt);
                Assert.Equal(cancelledAt, appointment.UpdatedAt);
                Assert.Equal(65m, appointment.TotalPriceSnapshot);
                Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);
                Assert.Single(appointment.Items);

                var record = await context.IdempotencyRecords
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal("CancelAppointment", record.Operation);
                Assert.Equal(key, record.Key);
                Assert.Equal(cancelledAt, record.CreatedAt);
                Assert.False(string.IsNullOrWhiteSpace(record.RequestHash));

                var storedResponse =
                    System.Text.Json.JsonSerializer
                        .Deserialize<CancelAppointmentResponse>(
                            record.ResponseJson);

                Assert.Equal(original, storedResponse);
                Assert.Equal(1, await context.Appointments.CountAsync());
                Assert.Equal(1, await context.AppointmentItems.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentExpiration_ShouldRespectDeadlineTenantAndIdempotency()
    {
        var dataA = new TestData();
        var dataB = new TestData();
        var appointmentAId = Guid.NewGuid();
        var appointmentBId = Guid.NewGuid();

        var expiresAt = CreatedAt.AddMinutes(10);
        var startsAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(dataA);
            await SeedAsync(dataB);

            await using (var context = CreateContext(dataA.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        dataA,
                        appointmentAId,
                        startsAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(dataB.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        dataB,
                        appointmentBId,
                        startsAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(dataA.TenantId))
            {
                var service = new AppointmentExpirationService(context);

                var beforeDeadline = await service.ExpireDueAsync(
                    expiresAt.AddSeconds(-1));

                Assert.Equal(0, beforeDeadline);
            }

            await using (var context = CreateContext(dataA.TenantId))
            {
                var service = new AppointmentExpirationService(context);

                var atDeadline = await service.ExpireDueAsync(
                    expiresAt.ToOffset(TimeSpan.FromHours(-3)));

                Assert.Equal(1, atDeadline);
            }

            await using (var context = CreateContext(dataA.TenantId))
            {
                var service = new AppointmentExpirationService(context);

                var repeated = await service.ExpireDueAsync(
                    expiresAt.AddMinutes(5));

                Assert.Equal(0, repeated);
            }

            await using (var context = CreateContext(dataA.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity =>
                        entity.Id == appointmentAId);

                Assert.Equal(
                    AppointmentStatus.Expired,
                    appointment.Status);

                Assert.False(appointment.OccupiesSchedule);
                Assert.Equal(expiresAt, appointment.UpdatedAt);
                Assert.Equal(CreatedAt, appointment.CreatedAt);

                Assert.Equal<DateTimeOffset?>(
                    expiresAt,
                    appointment.ReservationExpiresAt);

                Assert.Equal(65m, appointment.TotalPriceSnapshot);
                Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);
                Assert.Single(appointment.Items);
            }

            await using (var context = CreateContext(dataB.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity =>
                        entity.Id == appointmentBId);

                // A execução do tenant A não altera o tenant B.
                Assert.Equal(
                    AppointmentStatus.Pending,
                    appointment.Status);

                Assert.Equal(CreatedAt, appointment.UpdatedAt);

                var service = new AppointmentExpirationService(context);

                Assert.Equal(
                    1,
                    await service.ExpireDueAsync(expiresAt));
            }
        }
        finally
        {
            await CleanupAsync(dataA.TenantId);
            await CleanupAsync(dataB.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentCreation_WithConcurrentDifferentKeys_ShouldPersistOnlyOne()
    {
        var data = new TestData();

        var firstCommand = new CreateAppointmentCommand(
            data.CustomerId,
            data.LocationId,
            data.ProfessionalId,
            [data.ServiceId],
            new DateTimeOffset(
                2026, 10, 19, 12, 0, 0, TimeSpan.Zero),
            "appointment-concurrent-first");

        var secondCommand = firstCommand with
        {
            IdempotencyKey = "appointment-concurrent-second"
        };

        try
        {
            await SeedBookableAsync(data);

            var gate = new ConcurrentSaveGate();

            await using var firstContext =
                CreateContext(data.TenantId, gate);

            await using var secondContext =
                CreateContext(data.TenantId, gate);

            var firstTask = CreateHandler(firstContext)
                .HandleAsync(firstCommand);

            var secondTask = CreateHandler(secondContext)
                .HandleAsync(secondCommand);

            var results = await Task.WhenAll(firstTask, secondTask)
                .WaitAsync(TimeSpan.FromSeconds(60));

            Assert.Equal(2, gate.Arrivals);

            var winner = Assert.Single(
                results,
                result => result.IsSuccess);

            var loser = Assert.Single(
                results,
                result => !result.IsSuccess);

            Assert.NotNull(winner.Response);
            Assert.Equal(
                CreateAppointmentError.SlotUnavailable,
                loser.Error);
            Assert.Null(loser.Response);

            var winnerCommand = results[0].IsSuccess
                ? firstCommand
                : secondCommand;

            var loserCommand = results[0].IsSuccess
                ? secondCommand
                : firstCommand;

            await using var readContext = CreateContext(data.TenantId);

            var appointment = await readContext.Appointments
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(winner.Response.Id, appointment.Id);

            var item = await readContext.AppointmentItems
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(appointment.Id, item.AppointmentId);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(winnerCommand.IdempotencyKey, record.Key);
            Assert.Equal(
                AppointmentRequestHasher.Compute(winnerCommand),
                record.RequestHash);

            Assert.False(
                await readContext.IdempotencyRecords
                    .AnyAsync(entity =>
                        entity.Operation == "CreateAppointment" &&
                        entity.Key == loserCommand.IdempotencyKey));

            // A tentativa perdedora não deixa entidades pendentes no contexto.
            var losingContext = results[0].IsSuccess
                ? secondContext
                : firstContext;

            Assert.Empty(
                losingContext.ChangeTracker.Entries<Appointment>());

            Assert.Empty(
                losingContext.ChangeTracker.Entries<AppointmentItem>());

            Assert.Empty(
                losingContext.ChangeTracker.Entries<
                    VirtualEmployee.Infrastructure.Persistence
                        .Idempotency.IdempotencyRecord>());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentCreation_WithConcurrentSameKey_ShouldReplayWinner()
    {
        var data = new TestData();

        var command = new CreateAppointmentCommand(
            data.CustomerId,
            data.LocationId,
            data.ProfessionalId,
            [data.ServiceId],
            new DateTimeOffset(
                2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(-3)),
            "appointment-concurrent-same-key");

        try
        {
            await SeedBookableAsync(data);

            await using var firstContext = CreateContext(data.TenantId);
            await using var secondContext = CreateContext(data.TenantId);

            var firstHandler = CreateHandler(firstContext);
            var secondHandler = CreateHandler(secondContext);

            var startGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<CreateAppointmentResult> ExecuteAsync(
                CreateAppointmentHandler handler,
                CreateAppointmentCommand request)
            {
                await startGate.Task;

                return await handler.HandleAsync(request);
            }

            var firstTask = ExecuteAsync(firstHandler, command);

            var secondTask = ExecuteAsync(
                secondHandler,
                command with
                {
                    StartsAt = command.StartsAt.ToUniversalTime()
                });

            startGate.SetResult(true);

            var results = await Task.WhenAll(firstTask, secondTask)
                .WaitAsync(TimeSpan.FromSeconds(60));

            Assert.All(results, result =>
            {
                Assert.True(result.IsSuccess);
                Assert.Equal(CreateAppointmentError.None, result.Error);
                Assert.NotNull(result.Response);
            });

            var first = results[0].Response!;
            var second = results[1].Response!;

            Assert.Equal(first.Id, second.Id);
            Assert.Equal(first.StartsAt, second.StartsAt);
            Assert.Equal(first.EndsAt, second.EndsAt);
            Assert.Equal(first.Status, second.Status);
            Assert.Equal(first.TotalPrice, second.TotalPrice);
            Assert.Equal(
                first.TotalDurationMinutes,
                second.TotalDurationMinutes);
            Assert.Equal(
                first.ReservationExpiresAt,
                second.ReservationExpiresAt);
            Assert.Equal(first.CreatedAt, second.CreatedAt);
            Assert.Equal(first.UpdatedAt, second.UpdatedAt);
            Assert.Equal(
                first.Items.ToArray(),
                second.Items.ToArray());

            await using var readContext = CreateContext(data.TenantId);

            var persisted = await readContext.Appointments
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(first.Id, persisted.Id);

            var item = await readContext.AppointmentItems
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(first.Id, item.AppointmentId);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(command.IdempotencyKey, record.Key);
            Assert.Equal(
                AppointmentRequestHasher.Compute(command),
                record.RequestHash);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentCreation_ShouldReplayOriginalResponseAfterChanges()
    {
        var data = new TestData();

        var command = new CreateAppointmentCommand(
            data.CustomerId,
            data.LocationId,
            data.ProfessionalId,
            [data.ServiceId],
            new DateTimeOffset(
                2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(-3)),
            "appointment-replay-test");

        try
        {
            await SeedBookableAsync(data);

            AppointmentResponse original;

            await using (var context = CreateContext(data.TenantId))
            {
                var result = await CreateHandler(context)
                    .HandleAsync(command);

                Assert.True(result.IsSuccess);
                Assert.NotNull(result.Response);

                original = result.Response;
            }

            var expiredAt = CreatedAt.AddMinutes(10);

            await using (var context = CreateContext(data.TenantId))
            {
                var service = await context.Services
                    .SingleAsync(entity => entity.Id == data.ServiceId);

                service.Update(
                    "Corte alterado",
                    99m,
                    60,
                    false,
                    CreatedAt.AddHours(1));

                var appointment = await context.Appointments
                    .SingleAsync(entity => entity.Id == original.Id);

                Assert.True(appointment.Expire(expiredAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                // Mesmo instante, agora representado com offset UTC.
                var replayCommand = command with
                {
                    StartsAt = command.StartsAt.ToUniversalTime()
                };

                var result = await CreateHandler(
                        context,
                        now: command.StartsAt.AddDays(1))
                    .HandleAsync(replayCommand);

                Assert.True(result.IsSuccess);
                Assert.NotNull(result.Response);

                var replay = result.Response;

                Assert.Equal(original.Id, replay.Id);
                Assert.Equal(original.BusinessId, replay.BusinessId);
                Assert.Equal(original.LocationId, replay.LocationId);
                Assert.Equal(original.CustomerId, replay.CustomerId);
                Assert.Equal(original.ProfessionalId, replay.ProfessionalId);
                Assert.Equal(original.StartsAt, replay.StartsAt);
                Assert.Equal(original.EndsAt, replay.EndsAt);
                Assert.Equal(original.Status, replay.Status);
                Assert.Equal(original.TotalPrice, replay.TotalPrice);
                Assert.Equal(
                    original.TotalDurationMinutes,
                    replay.TotalDurationMinutes);
                Assert.Equal(
                    original.ReservationExpiresAt,
                    replay.ReservationExpiresAt);
                Assert.Equal(original.CreatedAt, replay.CreatedAt);
                Assert.Equal(original.UpdatedAt, replay.UpdatedAt);

                Assert.Equal(
                    original.Items.ToArray(),
                    replay.Items.ToArray());
            }

            await using var readContext = CreateContext(data.TenantId);

            var persisted = await readContext.Appointments
                .AsNoTracking()
                .Include(entity => entity.Items)
                .SingleAsync();

            // Replay não reativa a reserva expirada.
            Assert.Equal(original.Id, persisted.Id);
            Assert.Equal(AppointmentStatus.Expired, persisted.Status);
            Assert.Equal(expiredAt, persisted.UpdatedAt);

            // Os snapshots continuam independentes do catálogo.
            Assert.Equal(65m, persisted.TotalPriceSnapshot);
            Assert.Equal(45, persisted.TotalDurationMinutesSnapshot);

            var item = Assert.Single(persisted.Items);

            Assert.Equal("Corte", item.ServiceNameSnapshot);
            Assert.Equal(65m, item.PriceSnapshot);
            Assert.Equal(45, item.DurationMinutesSnapshot);

            Assert.Equal(
                1,
                await readContext.IdempotencyRecords.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentCreation_ShouldPersistReservationAndIdempotency()
    {
        var data = new TestData();

        var command = new CreateAppointmentCommand(
            data.CustomerId,
            data.LocationId,
            data.ProfessionalId,
            [data.ServiceId],
            new DateTimeOffset(
                2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(-3)),
            "appointment-create-test");

        try
        {
            await SeedBookableAsync(data);

            AppointmentResponse response;

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = CreateHandler(context);

                var result = await handler.HandleAsync(command);

                Assert.True(result.IsSuccess);
                Assert.Equal(CreateAppointmentError.None, result.Error);
                Assert.NotNull(result.Response);

                response = result.Response;

                Assert.NotEqual(Guid.Empty, response.Id);
                Assert.Equal(data.BusinessId, response.BusinessId);
                Assert.Equal(data.LocationId, response.LocationId);
                Assert.Equal(data.CustomerId, response.CustomerId);
                Assert.Equal(data.ProfessionalId, response.ProfessionalId);
                Assert.Equal("PENDING", response.Status);
                Assert.Equal(65m, response.TotalPrice);
                Assert.Equal(45, response.TotalDurationMinutes);

                Assert.Equal(
                    command.StartsAt.ToUniversalTime(),
                    response.StartsAt);

                Assert.Equal(
                    response.StartsAt.AddMinutes(45),
                    response.EndsAt);

                Assert.Equal(TimeSpan.Zero, response.StartsAt.Offset);

                Assert.Equal<DateTimeOffset?>(
                    CreatedAt.AddMinutes(10),
                    response.ReservationExpiresAt);

                Assert.Equal(CreatedAt, response.CreatedAt);
                Assert.Equal(CreatedAt, response.UpdatedAt);

                var item = Assert.Single(response.Items);

                Assert.Equal(data.ServiceId, item.ServiceId);
                Assert.Equal("Corte", item.Name);
                Assert.Equal(65m, item.Price);
                Assert.Equal(45, item.DurationMinutes);
            }

            await using var readContext = CreateContext(data.TenantId);

            var persisted = await readContext.Appointments
                .AsNoTracking()
                .Include(entity => entity.Items)
                .SingleAsync();

            Assert.Equal(response.Id, persisted.Id);
            Assert.Equal(response.StartsAt, persisted.StartsAt);
            Assert.Equal(response.EndsAt, persisted.EndsAt);
            Assert.Equal(response.TotalPrice, persisted.TotalPriceSnapshot);
            Assert.Equal(
                response.TotalDurationMinutes,
                persisted.TotalDurationMinutesSnapshot);

            Assert.Equal(
                data.ServiceId,
                Assert.Single(persisted.Items).ServiceId);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(data.TenantId, record.TenantId);
            Assert.Equal("CreateAppointment", record.Operation);
            Assert.Equal(command.IdempotencyKey, record.Key);

            Assert.Equal(
                AppointmentRequestHasher.Compute(command),
                record.RequestHash);

            var storedResponse =
                System.Text.Json.JsonSerializer
                    .Deserialize<AppointmentResponse>(
                        record.ResponseJson);

            Assert.NotNull(storedResponse);
            Assert.Equal(response.Id, storedResponse.Id);
            Assert.Equal(response.StartsAt, storedResponse.StartsAt);
            Assert.Equal(response.TotalPrice, storedResponse.TotalPrice);
            Assert.Equal(
                response.Items.ToArray(),
                storedResponse.Items.ToArray());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task Appointment_WithConcurrentOverlap_ShouldPersistOnlyOne()
    {
        var data = new TestData();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var startsAt = CreatedAt.AddHours(2);

        static Npgsql.PostgresException? FindPostgresException(
            Exception exception)
        {
            for (Exception? current = exception;
                 current is not null;
                 current = current.InnerException)
            {
                if (current is Npgsql.PostgresException postgresException)
                {
                    return postgresException;
                }
            }

            return null;
        }

        async Task<Exception?> TrySaveAsync(AppDbContext context)
        {
            try
            {
                await context.SaveChangesAsync();
                return null;
            }
            catch (Exception exception) when (
                FindPostgresException(exception) is
                {
                    SqlState:
                        Npgsql.PostgresErrorCodes.ExclusionViolation or
                        Npgsql.PostgresErrorCodes.DeadlockDetected
                })
            {
                return exception;
            }
        }

        try
        {
            await SeedAsync(data);

            var gate = new ConcurrentSaveGate();

            await using var firstContext =
                CreateContext(data.TenantId, gate);

            await using var secondContext =
                CreateContext(data.TenantId, gate);

            firstContext.Appointments.Add(
                CreateAppointment(data, firstId, startsAt));

            secondContext.Appointments.Add(
                CreateAppointment(data, secondId, startsAt));

            var results = await Task.WhenAll(
                TrySaveAsync(firstContext),
                TrySaveAsync(secondContext));

            Assert.Equal(2, gate.Arrivals);
            Assert.Single(results, result => result is null);

            var failure = Assert.Single(
                results,
                result => result is not null);

            Assert.NotNull(failure);

            var winnerId = results[0] is null ? firstId : secondId;
            var loserId = results[0] is null ? secondId : firstId;

            var postgresException = FindPostgresException(failure);

            Assert.NotNull(postgresException);

            if (postgresException.SqlState ==
                Npgsql.PostgresErrorCodes.DeadlockDetected)
            {
                // As duas operações concorrentes já terminaram.
                // Repete a perdedora usando um contexto novo, sem o gate.
                await using var retryContext =
                    CreateContext(data.TenantId);

                retryContext.Appointments.Add(
                    CreateAppointment(data, loserId, startsAt));

                var retryFailure = await TrySaveAsync(retryContext);

                Assert.NotNull(retryFailure);

                postgresException =
                    FindPostgresException(retryFailure);

                Assert.NotNull(postgresException);
            }

            Assert.Equal(
                Npgsql.PostgresErrorCodes.ExclusionViolation,
                postgresException.SqlState);

            Assert.Equal(
                "ex_appointments_no_overlap",
                postgresException.ConstraintName);

            await using var readContext =
                CreateContext(data.TenantId);

            var appointment = Assert.Single(
                await readContext.Appointments
                    .AsNoTracking()
                    .ToListAsync());

            Assert.Equal(winnerId, appointment.Id);

            var item = Assert.Single(
                await readContext.AppointmentItems
                    .AsNoTracking()
                    .ToListAsync());

            Assert.Equal(winnerId, item.AppointmentId);

            Assert.False(
                await readContext.Appointments
                    .AnyAsync(entity => entity.Id == loserId));

            Assert.False(
                await readContext.AppointmentItems
                    .AnyAsync(entity =>
                        entity.AppointmentId == loserId));
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task AppointmentAndItems_ShouldBeInvisibleToAnotherTenant()
    {
        var dataA = new TestData();
        var dataB = new TestData();
        var appointmentAId = Guid.NewGuid();
        var appointmentBId = Guid.NewGuid();
        var startsAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(dataA);
            await SeedAsync(dataB);

            await using (var context = CreateContext(dataA.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(dataA, appointmentAId, startsAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(dataB.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(dataB, appointmentBId, startsAt));

                await context.SaveChangesAsync();
            }

            await using (var contextA = CreateContext(dataA.TenantId))
            {
                var appointment = Assert.Single(
                    await contextA.Appointments
                        .AsNoTracking()
                        .Include(entity => entity.Items)
                        .ToListAsync());

                Assert.Equal(appointmentAId, appointment.Id);

                var item = Assert.Single(appointment.Items);
                Assert.Equal(dataA.TenantId, item.TenantId);
                Assert.Equal(appointmentAId, item.AppointmentId);

                Assert.False(
                    await contextA.Appointments
                        .AnyAsync(entity => entity.Id == appointmentBId));

                Assert.False(
                    await contextA.AppointmentItems
                        .AnyAsync(entity =>
                            entity.AppointmentId == appointmentBId));

                var directItem = Assert.Single(
                    await contextA.AppointmentItems
                        .AsNoTracking()
                        .ToListAsync());

                Assert.Equal(appointmentAId, directItem.AppointmentId);

                // Confirma que os dois agregados existem fisicamente.
                Assert.Equal(
                    2,
                    await contextA.Appointments
                        .IgnoreQueryFilters()
                        .CountAsync(entity =>
                            entity.Id == appointmentAId ||
                            entity.Id == appointmentBId));

                Assert.Equal(
                    2,
                    await contextA.AppointmentItems
                        .IgnoreQueryFilters()
                        .CountAsync(entity =>
                            entity.AppointmentId == appointmentAId ||
                            entity.AppointmentId == appointmentBId));
            }

            await using (var contextB = CreateContext(dataB.TenantId))
            {
                var appointment = Assert.Single(
                    await contextB.Appointments
                        .AsNoTracking()
                        .Include(entity => entity.Items)
                        .ToListAsync());

                Assert.Equal(appointmentBId, appointment.Id);
                Assert.Equal(
                    dataB.TenantId,
                    Assert.Single(appointment.Items).TenantId);

                Assert.False(
                    await contextB.Appointments
                        .AnyAsync(entity => entity.Id == appointmentAId));

                Assert.False(
                    await contextB.AppointmentItems
                        .AnyAsync(entity =>
                            entity.AppointmentId == appointmentAId));

                Assert.Equal(
                    appointmentBId,
                    Assert.Single(
                        await contextB.AppointmentItems
                            .AsNoTracking()
                            .ToListAsync()).AppointmentId);
            }
        }
        finally
        {
            await CleanupAsync(dataA.TenantId);
            await CleanupAsync(dataB.TenantId);
        }
    }

    [Fact]
    public async Task Appointment_WhenExpired_ShouldReleaseInterval()
    {
        var data = new TestData();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var startsAt = CreatedAt.AddHours(2);
        var expiredAt = CreatedAt.AddMinutes(10);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(data, firstId, startsAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .SingleAsync(entity => entity.Id == firstId);

                Assert.True(appointment.Expire(expiredAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(data, secondId, startsAt));

                await context.SaveChangesAsync();
            }

            await using var readContext = CreateContext(data.TenantId);

            var appointments = await readContext.Appointments
                .AsNoTracking()
                .ToListAsync();

            Assert.Equal(2, appointments.Count);

            var expired = Assert.Single(
                appointments,
                entity => entity.Id == firstId);

            Assert.Equal(AppointmentStatus.Expired, expired.Status);
            Assert.False(expired.OccupiesSchedule);
            Assert.Equal(expiredAt, expired.UpdatedAt);
            Assert.Equal(CreatedAt, expired.CreatedAt);

            var replacement = Assert.Single(
                appointments,
                entity => entity.Id == secondId);

            Assert.Equal(AppointmentStatus.Pending, replacement.Status);
            Assert.True(replacement.OccupiesSchedule);
            Assert.Equal(startsAt, replacement.StartsAt);

            Assert.Equal(
                2,
                await readContext.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task Appointment_WithOverlapInAnotherLocation_ShouldReject()
    {
        var data = new TestData();
        var otherLocationId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var startsAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Locations.Add(
                    new Location(
                        otherLocationId,
                        data.TenantId,
                        data.BusinessId,
                        "Other Location",
                        "BR",
                        "America/Sao_Paulo",
                        CreatedAt));

                await context.SaveChangesAsync();

                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        data.TenantId,
                        data.ProfessionalId,
                        otherLocationId,
                        CreatedAt));

                context.Appointments.Add(
                    CreateAppointment(data, firstId, startsAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    new Appointment(
                        secondId,
                        data.TenantId,
                        data.BusinessId,
                        otherLocationId,
                        data.CustomerId,
                        data.ProfessionalId,
                        startsAt,
                        CreatedAt,
                        TimeSpan.FromMinutes(10),
                        [
                            new AppointmentItem(
                                Guid.NewGuid(),
                                data.TenantId,
                                secondId,
                                data.ServiceId,
                                "Corte",
                                65m,
                                45)
                        ]));

                var exception =
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync());

                var postgresException =
                    Assert.IsType<Npgsql.PostgresException>(
                        exception.InnerException);

                Assert.Equal(
                    Npgsql.PostgresErrorCodes.ExclusionViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    "ex_appointments_no_overlap",
                    postgresException.ConstraintName);
            }

            await using var verificationContext =
                CreateContext(data.TenantId);

            var persisted = Assert.Single(
                await verificationContext.Appointments
                    .AsNoTracking()
                    .ToListAsync());

            Assert.Equal(firstId, persisted.Id);
            Assert.Equal(data.LocationId, persisted.LocationId);

            Assert.False(
                await verificationContext.AppointmentItems
                    .AnyAsync(item => item.AppointmentId == secondId));
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task Appointment_ShouldPersistPendingReservationAndSnapshots()
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var startsAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    new Appointment(
                        appointmentId,
                        data.TenantId,
                        data.BusinessId,
                        data.LocationId,
                        data.CustomerId,
                        data.ProfessionalId,
                        startsAt.ToOffset(TimeSpan.FromHours(-3)),
                        CreatedAt.ToOffset(TimeSpan.FromHours(-3)),
                        TimeSpan.FromMinutes(10),
                        [
                            new AppointmentItem(
                                itemId,
                                data.TenantId,
                                appointmentId,
                                data.ServiceId,
                                "Corte",
                                65m,
                                45)
                        ]));

                await context.SaveChangesAsync();
            }

            await using var readContext = CreateContext(data.TenantId);

            var appointment = await readContext.Appointments
                .AsNoTracking()
                .Include(entity => entity.Items)
                .SingleAsync(entity => entity.Id == appointmentId);

            Assert.Equal(data.TenantId, appointment.TenantId);
            Assert.Equal(data.BusinessId, appointment.BusinessId);
            Assert.Equal(data.LocationId, appointment.LocationId);
            Assert.Equal(data.CustomerId, appointment.CustomerId);
            Assert.Equal(data.ProfessionalId, appointment.ProfessionalId);

            Assert.Equal(AppointmentStatus.Pending, appointment.Status);
            Assert.True(appointment.OccupiesSchedule);
            Assert.Equal(startsAt, appointment.StartsAt);
            Assert.Equal(startsAt.AddMinutes(45), appointment.EndsAt);
            Assert.Equal(65m, appointment.TotalPriceSnapshot);
            Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);

            Assert.Equal<DateTimeOffset?>(
                CreatedAt.AddMinutes(10),
                appointment.ReservationExpiresAt);

            Assert.Equal(CreatedAt, appointment.CreatedAt);
            Assert.Equal(CreatedAt, appointment.UpdatedAt);
            Assert.Equal(TimeSpan.Zero, appointment.StartsAt.Offset);
            Assert.Equal(TimeSpan.Zero, appointment.EndsAt.Offset);

            var item = Assert.Single(appointment.Items);

            Assert.Equal(itemId, item.Id);
            Assert.Equal(data.TenantId, item.TenantId);
            Assert.Equal(appointmentId, item.AppointmentId);
            Assert.Equal(data.ServiceId, item.ServiceId);
            Assert.Equal("Corte", item.ServiceNameSnapshot);
            Assert.Equal(65m, item.PriceSnapshot);
            Assert.Equal(45, item.DurationMinutesSnapshot);

            Assert.Equal(
                1,
                await readContext.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData(0, false)]
    [InlineData(10, false)]
    [InlineData(10, true)]
    public async Task PaymentCreation_WithLateGatewayResponse_ShouldPreserveReferenceWithoutConfirming(
        int offsetTicks,
        bool materializeAppointmentExpiration)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var providerPaymentId = Guid.NewGuid().ToString("N");
        var expiresAt = CreatedAt.AddMinutes(10);
        var lateNow = expiresAt.AddTicks(offsetTicks);

        var clock = new MutablePaymentTimeProvider(
            CreatedAt.AddMinutes(2));

        var command = new CreatePaymentAttemptCommand(
            appointmentId,
            PaymentMethod.Pix,
            Guid.NewGuid().ToString("N"));

        try
        {
            await SeedAsync(data);

            await using (var seedContext = CreateContext(data.TenantId))
            {
                seedContext.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await seedContext.SaveChangesAsync();
            }

            await using var context = CreateContext(data.TenantId);

            var gateway = new RecordingPaymentGateway(
                async (request, cancellationToken) =>
                {
                    Assert.Null(context.Database.CurrentTransaction);
                    Assert.Equal(expiresAt, request.ExpiresAt);

                    // Simula o tempo decorrido durante a chamada externa.
                    clock.UtcNow = lateNow;

                    if (materializeAppointmentExpiration)
                    {
                        await using var expirationContext =
                            CreateContext(data.TenantId);

                        var appointment =
                            await expirationContext.Appointments
                                .SingleAsync(
                                    entity => entity.Id == appointmentId,
                                    cancellationToken);

                        Assert.True(appointment.Expire(lateNow));

                        await expirationContext.SaveChangesAsync(
                            cancellationToken);
                    }

                    return CreateGatewayPaymentResult.Created(
                        new GatewayPaymentReference(
                            "gateway-test",
                            providerPaymentId,
                            "https://checkout.example.test/late",
                            request.ExpiresAt));
                });

            var service = new CreatePaymentAttemptService(
                context,
                gateway,
                clock);

            var result = await service.CreateAsync(command);

            Assert.False(result.IsSuccess);
            Assert.Equal(
                CreatePaymentAttemptError.ReservationExpired,
                result.Error);
            Assert.Null(result.Response);
            Assert.Null(context.Database.CurrentTransaction);

            await using (var readContext = CreateContext(data.TenantId))
            {
                var payment = Assert.Single(
                    await readContext.Payments
                        .AsNoTracking()
                        .ToListAsync());

                var attempt = Assert.Single(
                    await readContext.PaymentAttempts
                        .AsNoTracking()
                        .ToListAsync());

                var appointment = await readContext.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(PaymentStatus.Pending, payment.Status);
                Assert.Null(payment.ConfirmedPaymentAttemptId);
                Assert.Null(payment.ConfirmedAt);

                Assert.Equal(
                    PaymentAttemptStatus.Expired,
                    attempt.Status);

                Assert.Equal("gateway-test", attempt.Provider);
                Assert.Equal(providerPaymentId, attempt.ProviderPaymentId);
                Assert.Equal(expiresAt, attempt.ExpiresAt);
                Assert.Equal(lateNow, attempt.UpdatedAt);
                Assert.Null(attempt.ConfirmedAt);

                Assert.Equal(
                    materializeAppointmentExpiration
                        ? AppointmentStatus.Expired
                        : AppointmentStatus.Pending,
                    appointment.Status);

                Assert.Equal(
                    materializeAppointmentExpiration
                        ? lateNow
                        : CreatedAt,
                    appointment.UpdatedAt);

                Assert.Equal(
                    expiresAt,
                    appointment.ReservationExpiresAt);

                Assert.Equal(
                    2,
                    await readContext.IdempotencyRecords.CountAsync());

                Assert.True(
                    await readContext.IdempotencyRecords.AnyAsync(
                        entity =>
                            entity.Operation ==
                                PaymentAttemptCompletionService.Operation &&
                            entity.Key == command.IdempotencyKey));
            }

            // O replay da resposta final funciona mesmo após o prazo.
            await using (var replayContext = CreateContext(data.TenantId))
            {
                var replayService = new CreatePaymentAttemptService(
                    replayContext,
                    gateway,
                    new FixedTimeProvider(lateNow.AddMinutes(1)));

                var replay = await replayService.CreateAsync(command);

                Assert.False(replay.IsSuccess);
                Assert.Equal(
                    CreatePaymentAttemptError.ReservationExpired,
                    replay.Error);
                Assert.Null(replay.Response);
            }

            Assert.Single(gateway.Requests);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("http-error")]
    [InlineData("gateway-cancellation")]
    [InlineData("unknown")]
    public async Task PaymentCreation_WithUncertainOutcome_ShouldReuseOriginalAttemptOnRetry(
        string scenario)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var providerPaymentId = Guid.NewGuid().ToString("N");
        var now = CreatedAt.AddMinutes(2);

        var command = new CreatePaymentAttemptCommand(
            appointmentId,
            PaymentMethod.Pix,
            Guid.NewGuid().ToString("N"));

        CreateGatewayPaymentRequest? externalRequest = null;
        GatewayPaymentReference? externalReference = null;
        var gatewayCalls = 0;

        var gateway = new RecordingPaymentGateway(
            (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var call = Interlocked.Increment(ref gatewayCalls);

                if (call == 1)
                {
                    // Simula uma cobrança criada externamente,
                    // cuja resposta não chegou ao backend.
                    externalRequest = request;

                    externalReference = new GatewayPaymentReference(
                        "gateway-test",
                        providerPaymentId,
                        "https://checkout.example.test/recovered",
                        request.ExpiresAt);

                    return scenario switch
                    {
                        "timeout" =>
                            Task.FromException<CreateGatewayPaymentResult>(
                                new TimeoutException(
                                    "Simulated gateway timeout.")),

                        "http-error" =>
                            Task.FromException<CreateGatewayPaymentResult>(
                                new System.Net.Http.HttpRequestException(
                                    "Simulated connection failure.")),

                        "gateway-cancellation" =>
                            Task.FromException<CreateGatewayPaymentResult>(
                                new OperationCanceledException(
                                    "Simulated gateway timeout cancellation.")),

                        "unknown" =>
                            Task.FromResult(
                                CreateGatewayPaymentResult.Unknown()),

                        _ => throw new ArgumentOutOfRangeException(
                            nameof(scenario))
                    };
                }

                // Simula o provedor reconhecendo a mesma chave:
                // devolve a cobrança anterior sem criar outra.
                Assert.NotNull(externalRequest);
                Assert.NotNull(externalReference);
                Assert.Equal(externalRequest, request);
                Assert.Equal(
                    externalRequest.IdempotencyKey,
                    request.IdempotencyKey);

                return Task.FromResult(
                    CreateGatewayPaymentResult.Created(
                        externalReference));
            });

        try
        {
            await SeedAsync(data);

            await using (var seedContext = CreateContext(data.TenantId))
            {
                seedContext.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await seedContext.SaveChangesAsync();
            }

            await using (var firstContext = CreateContext(data.TenantId))
            {
                var service = new CreatePaymentAttemptService(
                    firstContext,
                    gateway,
                    new FixedTimeProvider(now));

                var first = await service.CreateAsync(command);

                Assert.False(first.IsSuccess);
                Assert.Equal(
                    CreatePaymentAttemptError.ProviderError,
                    first.Error);
                Assert.Null(first.Response);
                Assert.Null(firstContext.Database.CurrentTransaction);
            }

            Assert.Equal(1, gatewayCalls);
            Assert.NotNull(externalRequest);

            await using (var readContext = CreateContext(data.TenantId))
            {
                var payment = Assert.Single(
                    await readContext.Payments
                        .AsNoTracking()
                        .ToListAsync());

                var attempt = Assert.Single(
                    await readContext.PaymentAttempts
                        .AsNoTracking()
                        .ToListAsync());

                var intent = Assert.Single(
                    await readContext.IdempotencyRecords
                        .AsNoTracking()
                        .ToListAsync());

                Assert.Equal(externalRequest.PaymentId, payment.Id);
                Assert.Equal(externalRequest.PaymentAttemptId, attempt.Id);
                Assert.Equal(PaymentStatus.Pending, payment.Status);
                Assert.Equal(PaymentAttemptStatus.Created, attempt.Status);
                Assert.Null(attempt.Provider);
                Assert.Null(attempt.ProviderPaymentId);

                Assert.Equal(
                    PaymentAttemptPreparationService.IntentOperation,
                    intent.Operation);

                Assert.False(
                    await readContext.IdempotencyRecords.AnyAsync(
                        entity =>
                            entity.Operation ==
                                PaymentAttemptCompletionService.Operation));
            }

            CreatePaymentAttemptResponse recoveredResponse;

            await using (var retryContext = CreateContext(data.TenantId))
            {
                var service = new CreatePaymentAttemptService(
                    retryContext,
                    gateway,
                    new FixedTimeProvider(now.AddMinutes(1)));

                var retry = await service.CreateAsync(command);

                Assert.True(retry.IsSuccess);

                recoveredResponse =
                    Assert.IsType<CreatePaymentAttemptResponse>(
                        retry.Response);

                Assert.Equal(
                    externalRequest.PaymentId,
                    recoveredResponse.PaymentId);

                Assert.Equal(
                    externalRequest.PaymentAttemptId,
                    recoveredResponse.PaymentAttemptId);

                Assert.Equal(
                    externalRequest.Amount,
                    recoveredResponse.Amount);

                Assert.Equal(
                    "https://checkout.example.test/recovered",
                    recoveredResponse.CheckoutUrl);

                var attempt = Assert.Single(
                    await retryContext.PaymentAttempts
                        .AsNoTracking()
                        .ToListAsync());

                Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
                Assert.Equal(providerPaymentId, attempt.ProviderPaymentId);
                Assert.Equal(now, attempt.CreatedAt);
                Assert.Equal(now.AddMinutes(1), attempt.UpdatedAt);

                Assert.Equal(1, await retryContext.Payments.CountAsync());
                Assert.Equal(2, await retryContext.IdempotencyRecords.CountAsync());
            }

            Assert.Equal(2, gatewayCalls);
            Assert.Equal(2, gateway.Requests.Count);

            await using (var replayContext = CreateContext(data.TenantId))
            {
                var service = new CreatePaymentAttemptService(
                    replayContext,
                    gateway,
                    new FixedTimeProvider(now.AddMinutes(2)));

                var replay = await service.CreateAsync(command);

                Assert.True(replay.IsSuccess);
                Assert.Equal(recoveredResponse, replay.Response);
            }

            Assert.Equal(2, gatewayCalls);
            Assert.Equal(2, gateway.Requests.Count);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(-10, true)]
    [InlineData(0, false)]
    [InlineData(10, false)]
    public async Task PaymentPreparation_ShouldRespectExactReservationDeadline(
        int offsetTicks,
        bool expectedSuccess)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var expiresAt = CreatedAt.AddMinutes(10);
        var now = expiresAt.AddTicks(offsetTicks);

        var command = new CreatePaymentAttemptCommand(
            appointmentId,
            PaymentMethod.Pix,
            Guid.NewGuid().ToString("N"));

        try
        {
            await SeedAsync(data);

            await using (var seedContext = CreateContext(data.TenantId))
            {
                var appointment = CreateAppointment(
                    data,
                    appointmentId,
                    CreatedAt.AddHours(2));

                Assert.Equal(
                    expiresAt,
                    appointment.ReservationExpiresAt);

                seedContext.Appointments.Add(appointment);
                await seedContext.SaveChangesAsync();
            }

            await using (var preparationContext =
                CreateContext(data.TenantId))
            {
                var service = new PaymentAttemptPreparationService(
                    preparationContext,
                    new FixedTimeProvider(now));

                var result = await service.PrepareAsync(command);

                Assert.Equal(expectedSuccess, result.IsSuccess);
                Assert.Null(
                    preparationContext.Database.CurrentTransaction);

                if (expectedSuccess)
                {
                    Assert.Equal(
                        CreatePaymentAttemptError.None,
                        result.Error);

                    var request =
                        Assert.IsType<CreateGatewayPaymentRequest>(
                            result.Request);

                    Assert.Equal(expiresAt, request.ExpiresAt);
                }
                else
                {
                    Assert.Equal(
                        CreatePaymentAttemptError.ReservationExpired,
                        result.Error);

                    Assert.Null(result.Request);
                }
            }

            await using (var readContext = CreateContext(data.TenantId))
            {
                var expectedCount = expectedSuccess ? 1 : 0;

                Assert.Equal(
                    expectedCount,
                    await readContext.Payments.CountAsync());

                Assert.Equal(
                    expectedCount,
                    await readContext.PaymentAttempts.CountAsync());

                Assert.Equal(
                    expectedCount,
                    await readContext.IdempotencyRecords.CountAsync());

                var appointment = await readContext.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == appointmentId);

                // A preparação valida o prazo, mas não materializa
                // a expiração operacional do agendamento.
                Assert.Equal(
                    AppointmentStatus.Pending,
                    appointment.Status);

                Assert.Equal(
                    expiresAt,
                    appointment.ReservationExpiresAt);

                Assert.Equal(CreatedAt, appointment.UpdatedAt);

                if (expectedSuccess)
                {
                    var attempt = await readContext.PaymentAttempts
                        .AsNoTracking()
                        .SingleAsync();

                    Assert.Equal(now, attempt.CreatedAt);
                    Assert.Equal(expiresAt, attempt.ExpiresAt);
                    Assert.True(attempt.CreatedAt < attempt.ExpiresAt);
                    Assert.Equal(
                        PaymentAttemptStatus.Created,
                        attempt.Status);
                }
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(PaymentMethod.Pix)]
    [InlineData(PaymentMethod.CreditCard)]
    public async Task PaymentPreparation_WithNewKeyAndUnresolvedAttempt_ShouldConflict(
        PaymentMethod nextMethod)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var now = CreatedAt.AddMinutes(2);

        var originalCommand = new CreatePaymentAttemptCommand(
            appointmentId,
            PaymentMethod.Pix,
            Guid.NewGuid().ToString("N"));

        var nextCommand = new CreatePaymentAttemptCommand(
            appointmentId,
            nextMethod,
            Guid.NewGuid().ToString("N"));

        try
        {
            await SeedAsync(data);

            await using (var seedContext = CreateContext(data.TenantId))
            {
                seedContext.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await seedContext.SaveChangesAsync();
            }

            CreateGatewayPaymentRequest originalRequest;

            await using (var firstContext = CreateContext(data.TenantId))
            {
                var service = new PaymentAttemptPreparationService(
                    firstContext,
                    new FixedTimeProvider(now));

                var first = await service.PrepareAsync(originalCommand);

                Assert.True(first.IsSuccess);

                originalRequest =
                    Assert.IsType<CreateGatewayPaymentRequest>(
                        first.Request);
            }

            await using (var nextContext = CreateContext(data.TenantId))
            {
                var service = new PaymentAttemptPreparationService(
                    nextContext,
                    new FixedTimeProvider(now.AddMinutes(1)));

                var next = await service.PrepareAsync(nextCommand);

                Assert.False(next.IsSuccess);
                Assert.Equal(
                    CreatePaymentAttemptError.ConcurrencyConflict,
                    next.Error);
                Assert.Null(next.Request);
                Assert.Null(nextContext.Database.CurrentTransaction);
            }

            await using (var readContext = CreateContext(data.TenantId))
            {
                var payment = Assert.Single(
                    await readContext.Payments
                        .AsNoTracking()
                        .ToListAsync());

                var attempt = Assert.Single(
                    await readContext.PaymentAttempts
                        .AsNoTracking()
                        .ToListAsync());

                var intent = Assert.Single(
                    await readContext.IdempotencyRecords
                        .AsNoTracking()
                        .ToListAsync());

                Assert.Equal(originalRequest.PaymentId, payment.Id);
                Assert.Equal(PaymentStatus.Pending, payment.Status);

                Assert.Equal(originalRequest.PaymentAttemptId, attempt.Id);
                Assert.Equal(PaymentAttemptStatus.Created, attempt.Status);
                Assert.Equal(PaymentMethod.Pix, attempt.PaymentMethod);
                Assert.Equal(now, attempt.CreatedAt);
                Assert.Equal(now, attempt.UpdatedAt);
                Assert.Null(attempt.Provider);
                Assert.Null(attempt.ProviderPaymentId);

                Assert.Equal(originalCommand.IdempotencyKey, intent.Key);
                Assert.Equal(
                    PaymentAttemptPreparationService.IntentOperation,
                    intent.Operation);

                Assert.False(
                    await readContext.IdempotencyRecords.AnyAsync(
                        entity => entity.Key == nextCommand.IdempotencyKey));

                var storedRequest =
                    System.Text.Json.JsonSerializer
                        .Deserialize<CreateGatewayPaymentRequest>(
                            intent.ResponseJson);

                Assert.Equal(originalRequest, storedRequest);
            }

            await using (var replayContext = CreateContext(data.TenantId))
            {
                var service = new PaymentAttemptPreparationService(
                    replayContext,
                    new FixedTimeProvider(now.AddMinutes(2)));

                var replay = await service.PrepareAsync(originalCommand);

                Assert.True(replay.IsSuccess);
                Assert.Equal(originalRequest, replay.Request);

                Assert.Equal(
                    1,
                    await replayContext.PaymentAttempts.CountAsync());

                Assert.Equal(
                    1,
                    await replayContext.IdempotencyRecords.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("appointment")]
    [InlineData("method")]
    public async Task PaymentPreparation_WithSameKeyAndDifferentContent_ShouldConflict(
        string scenario)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var otherAppointmentId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString("N");
        var now = CreatedAt.AddMinutes(2);

        var originalCommand = new CreatePaymentAttemptCommand(
            appointmentId,
            PaymentMethod.Pix,
            key);

        try
        {
            await SeedAsync(data);

            await using (var seedContext = CreateContext(data.TenantId))
            {
                seedContext.Appointments.AddRange(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)),
                    CreateAppointment(
                        data,
                        otherAppointmentId,
                        CreatedAt.AddHours(4)));

                await seedContext.SaveChangesAsync();
            }

            CreateGatewayPaymentRequest originalRequest;

            await using (var firstContext = CreateContext(data.TenantId))
            {
                var service = new PaymentAttemptPreparationService(
                    firstContext,
                    new FixedTimeProvider(now));

                var original = await service.PrepareAsync(originalCommand);

                Assert.True(original.IsSuccess);

                originalRequest =
                    Assert.IsType<CreateGatewayPaymentRequest>(
                        original.Request);
            }

            var conflictingCommand = scenario == "appointment"
                ? originalCommand with
                {
                    AppointmentId = otherAppointmentId
                }
                : originalCommand with
                {
                    PaymentMethod = PaymentMethod.CreditCard
                };

            await using (var conflictContext = CreateContext(data.TenantId))
            {
                var service = new PaymentAttemptPreparationService(
                    conflictContext,
                    new FixedTimeProvider(now.AddMinutes(1)));

                var conflict = await service.PrepareAsync(
                    conflictingCommand);

                Assert.False(conflict.IsSuccess);
                Assert.Equal(
                    CreatePaymentAttemptError.IdempotencyConflict,
                    conflict.Error);
                Assert.Null(conflict.Request);
                Assert.Null(conflictContext.Database.CurrentTransaction);
            }

            await using (var readContext = CreateContext(data.TenantId))
            {
                var payment = Assert.Single(
                    await readContext.Payments
                        .AsNoTracking()
                        .ToListAsync());

                var attempt = Assert.Single(
                    await readContext.PaymentAttempts
                        .AsNoTracking()
                        .ToListAsync());

                var intent = Assert.Single(
                    await readContext.IdempotencyRecords
                        .AsNoTracking()
                        .ToListAsync());

                Assert.Equal(originalRequest.PaymentId, payment.Id);
                Assert.Equal(appointmentId, payment.AppointmentId);
                Assert.Equal(PaymentStatus.Pending, payment.Status);

                Assert.Equal(originalRequest.PaymentAttemptId, attempt.Id);
                Assert.Equal(PaymentMethod.Pix, attempt.PaymentMethod);
                Assert.Equal(PaymentAttemptStatus.Created, attempt.Status);
                Assert.Equal(now, attempt.CreatedAt);
                Assert.Equal(now, attempt.UpdatedAt);

                Assert.Equal(key, intent.Key);
                Assert.Equal(
                    PaymentAttemptPreparationService.IntentOperation,
                    intent.Operation);

                var storedRequest =
                    System.Text.Json.JsonSerializer
                        .Deserialize<CreateGatewayPaymentRequest>(
                            intent.ResponseJson);

                Assert.Equal(originalRequest, storedRequest);

                Assert.False(
                    await readContext.Payments.AnyAsync(
                        entity =>
                            entity.AppointmentId == otherAppointmentId));
            }

            // O conflito não inutiliza a chave original.
            await using (var replayContext = CreateContext(data.TenantId))
            {
                var service = new PaymentAttemptPreparationService(
                    replayContext,
                    new FixedTimeProvider(now.AddMinutes(2)));

                var replay = await service.PrepareAsync(originalCommand);

                Assert.True(replay.IsSuccess);
                Assert.Equal(originalRequest, replay.Request);
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("payment")]
    [InlineData("amount")]
    public async Task PaymentAttempt_WithInvalidPaymentReference_ShouldBeRejected(
        string scenario)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        try
        {
            await SeedAsync(data);

            await using var context = CreateContext(data.TenantId);
            await using var transaction =
                await context.Database.BeginTransactionAsync();

            var appointment = CreateAppointment(
                data,
                appointmentId,
                CreatedAt.AddHours(2));

            context.Appointments.Add(appointment);
            await context.SaveChangesAsync();

            var payment = new Payment(
                paymentId,
                appointment,
                CreatedAt.AddMinutes(1));

            context.Payments.Add(payment);
            await context.SaveChangesAsync();

            var attemptTenantId = scenario == "tenant"
                ? Guid.NewGuid()
                : data.TenantId;

            var attemptPaymentId = scenario == "payment"
                ? Guid.NewGuid()
                : paymentId;

            var attemptAmount = scenario == "amount"
                ? payment.Amount + 0.01m
                : payment.Amount;

            var now = CreatedAt.AddMinutes(2);
            var expiresAt = CreatedAt.AddMinutes(10);

            var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(
                async () =>
                {
                    await context.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        INSERT INTO payment_attempts
                        (
                            id,
                            tenant_id,
                            payment_id,
                            payment_method,
                            status,
                            amount,
                            expires_at,
                            created_at,
                            updated_at
                        )
                        VALUES
                        (
                            {attemptId},
                            {attemptTenantId},
                            {attemptPaymentId},
                            'PIX',
                            'CREATED',
                            {attemptAmount},
                            {expiresAt},
                            {now},
                            {now}
                        );
                        """);
                });

            Assert.Equal(
                Npgsql.PostgresErrorCodes.ForeignKeyViolation,
                exception.SqlState);

            Assert.Equal(
                "FK_payment_attempts_payments_tenant_id_payment_id_amount",
                exception.ConstraintName);

            // Após a falha SQL, reverte a transação antes de novas consultas.
            await transaction.RollbackAsync();

            Assert.False(
                await context.PaymentAttempts
                    .IgnoreQueryFilters()
                    .AnyAsync(entity => entity.Id == attemptId));
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(AppointmentStatus.CancelledByClient)]
    [InlineData(AppointmentStatus.CancelledByBusiness)]
    public async Task AppointmentHistory_ShouldRecordCancellationOnlyOnce(
        AppointmentStatus cancellationStatus)
    {
        var data = new TestData();

        var startsAt = new DateTimeOffset(
            2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        var now = CreatedAt.AddMinutes(5);
        const string reason = "Cancelamento para validar histórico";

        try
        {
            await SeedBookableAsync(data);

            Guid appointmentId;

            await using (var context = CreateContext(data.TenantId))
            {
                var creation = await CreateHandler(context).HandleAsync(
                    new CreateAppointmentCommand(
                        data.CustomerId,
                        data.LocationId,
                        data.ProfessionalId,
                        new[] { data.ServiceId },
                        startsAt,
                        Guid.NewGuid().ToString()));

                Assert.True(
                    creation.IsSuccess,
                    $"Criação falhou: {creation.Error}");

                Assert.NotNull(creation.Response);
                appointmentId = creation.Response.Id;
            }

            var command = new CancelAppointmentCommand(
                appointmentId,
                cancellationStatus,
                reason,
                Guid.NewGuid().ToString());

            // Cancelamento, replay e repetição com uma nova chave.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await using var context = CreateContext(data.TenantId);

                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(now.AddSeconds(attempt))));

                var request = attempt == 2
                    ? command with
                    {
                        IdempotencyKey = Guid.NewGuid().ToString()
                    }
                    : command;

                var result = await handler.HandleAsync(request);

                Assert.True(
                    result.IsSuccess,
                    $"Tentativa {attempt}: {result.Error}");

                Assert.NotNull(result.Response);
                Assert.Equal(appointmentId, result.Response.Id);
                Assert.Equal(reason, result.Response.CancellationReason);
                Assert.Equal(now, result.Response.CancelledAt);
                Assert.Equal(now, result.Response.UpdatedAt);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == appointmentId);

                var entries = await context.AppointmentHistoryEntries
                    .AsNoTracking()
                    .Where(entry => entry.AppointmentId == appointmentId)
                    .OrderBy(entry => entry.OccurredAt)
                    .ToListAsync();

                Assert.Equal(2, entries.Count);

                var creation = Assert.Single(
                    entries,
                    entry => entry.Type == AppointmentHistoryType.Created);

                var cancellation = Assert.Single(
                    entries,
                    entry => entry.Type == AppointmentHistoryType.Cancelled);

                Assert.Null(creation.Before);
                Assert.Equal(
                    AppointmentStatus.Pending,
                    creation.After.Status);

                Assert.NotNull(cancellation.Before);
                Assert.Equal(creation.After, cancellation.Before);
                Assert.Equal(cancellationStatus, cancellation.After.Status);
                Assert.Equal(
                    AppointmentHistoryState.Capture(appointment),
                    cancellation.After);

                Assert.Equal(data.TenantId, cancellation.TenantId);
                Assert.Equal(now, cancellation.OccurredAt);
                Assert.Equal(reason, cancellation.Reason);

                Assert.Equal(
                    cancellation.Before.StartsAt,
                    cancellation.After.StartsAt);
                Assert.Equal(
                    cancellation.Before.EndsAt,
                    cancellation.After.EndsAt);
                Assert.Equal(
                    cancellation.Before.ReservationExpiresAt,
                    cancellation.After.ReservationExpiresAt);

                Assert.Equal(cancellationStatus, appointment.Status);
                Assert.Equal(now, appointment.CancelledAt);
                Assert.Equal(now, appointment.UpdatedAt);
                Assert.Equal(65m, appointment.TotalPriceSnapshot);
                Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);

                // A mesma chave é replay; a nova chave ganha sua resposta,
                // mas não gera outra alteração nem outro histórico.
                Assert.Equal(
                    2,
                    await context.IdempotencyRecords.CountAsync(
                        record => record.Operation == "CancelAppointment"));
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppointmentHistory_ShouldRejectModificationOrDeletion(
        bool delete)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var historyId = Guid.NewGuid();

        var startsAt = new DateTimeOffset(
            2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = CreateAppointment(
                    data,
                    appointmentId,
                    startsAt);

                context.Appointments.Add(appointment);

                context.AppointmentHistoryEntries.Add(
                    new AppointmentHistoryEntry(
                        historyId,
                        data.TenantId,
                        appointmentId,
                        AppointmentHistoryType.Created,
                        CreatedAt,
                        null,
                        AppointmentHistoryState.Capture(appointment)));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var history = await context.AppointmentHistoryEntries
                    .SingleAsync(entry => entry.Id == historyId);

                if (delete)
                {
                    context.AppointmentHistoryEntries.Remove(history);
                }
                else
                {
                    context.Entry(history)
                        .Property(entry => entry.OccurredAt)
                        .CurrentValue = CreatedAt.AddMinutes(1);
                }

                var exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => context.SaveChangesAsync());

                Assert.Equal(
                    "Appointment history entries cannot be modified or deleted.",
                    exception.Message);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.AppointmentHistoryEntries
                    .AsNoTracking()
                    .SingleAsync(entry => entry.Id == historyId);

                Assert.Equal(CreatedAt, persisted.OccurredAt);
                Assert.Equal(AppointmentHistoryType.Created, persisted.Type);
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("location-block")]
    [InlineData("professional-block")]
    [InlineData("outside-working-hours")]
    [InlineData("outside-start-grid")]
    public async Task AppointmentReschedulingService_WithUnavailableDestination_ShouldPreserveOriginalBooking(
        string scenario)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var requestedStart = scenario switch
        {
            "outside-working-hours" => originalStart.AddHours(-1),
            "outside-start-grid" => originalStart.AddMinutes(7),
            _ => originalStart.AddHours(1)
        };

        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        originalStart));

                await context.SaveChangesAsync();

                if (scenario is "location-block" or "professional-block")
                {
                    context.ScheduleBlocks.Add(
                        new ScheduleBlock(
                            Guid.NewGuid(),
                            data.TenantId,
                            data.LocationId,
                            scenario == "professional-block"
                                ? data.ProfessionalId
                                : null,
                            requestedStart,
                            requestedStart.AddMinutes(30),
                            CreatedAt));

                    await context.SaveChangesAsync();
                }
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                requestedStart,
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(command);

                Assert.Equal(
                    RescheduleAppointmentError.SlotUnavailable,
                    result.Error);
                Assert.Null(result.Response);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(originalStart, persisted.StartsAt);
                Assert.Equal(
                    originalStart.AddMinutes(45),
                    persisted.EndsAt);
                Assert.Equal(data.LocationId, persisted.LocationId);
                Assert.Equal(
                    data.ProfessionalId,
                    persisted.ProfessionalId);
                Assert.Equal(AppointmentStatus.Pending, persisted.Status);
                Assert.Equal(CreatedAt, persisted.UpdatedAt);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    persisted.ReservationExpiresAt);
                Assert.Equal(65m, persisted.TotalPriceSnapshot);
                Assert.Equal(45, persisted.TotalDurationMinutesSnapshot);
                Assert.Single(persisted.Items);

                Assert.Equal(
                    0,
                    await context.IdempotencyRecords.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(
        "missing-location",
        RescheduleAppointmentError.LocationNotFound)]
    [InlineData(
        "missing-professional",
        RescheduleAppointmentError.ProfessionalNotFound)]
    [InlineData(
        "inactive-location",
        RescheduleAppointmentError.InactiveLocation)]
    [InlineData(
        "inactive-professional",
        RescheduleAppointmentError.InactiveProfessional)]
    [InlineData(
        "service-not-offered",
        RescheduleAppointmentError.ServicesNotOffered)]
    [InlineData(
        "professional-not-eligible",
        RescheduleAppointmentError.ProfessionalNotEligible)]
    public async Task AppointmentReschedulingService_WithInvalidResources_ShouldPreserveOriginalBooking(
        string scenario,
        RescheduleAppointmentError expectedError)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        originalStart));

                await context.SaveChangesAsync();

                switch (scenario)
                {
                    case "inactive-location":
                    {
                        var location = await context.Locations
                            .SingleAsync(entity => entity.Id == data.LocationId);

                        location.Update(
                            "Unidade inativa",
                            "BR",
                            "America/Sao_Paulo",
                            false,
                            now);

                        break;
                    }

                    case "inactive-professional":
                    {
                        var professional = await context.Professionals
                            .SingleAsync(entity =>
                                entity.Id == data.ProfessionalId);

                        professional.Update(
                            "Profissional inativo",
                            false,
                            now);

                        break;
                    }

                    case "service-not-offered":
                    {
                        var link = await context.LocationServices
                            .SingleAsync(entity =>
                                entity.LocationId == data.LocationId &&
                                entity.ServiceId == data.ServiceId);

                        context.LocationServices.Remove(link);
                        break;
                    }

                    case "professional-not-eligible":
                    {
                        var link = await context.ProfessionalServices
                            .SingleAsync(entity =>
                                entity.ProfessionalId == data.ProfessionalId &&
                                entity.ServiceId == data.ServiceId);

                        link.Update(false, now);
                        break;
                    }
                }

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                scenario == "missing-location"
                    ? Guid.NewGuid()
                    : data.LocationId,
                scenario == "missing-professional"
                    ? Guid.NewGuid()
                    : data.ProfessionalId,
                originalStart.AddHours(1),
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(command);

                Assert.Equal(expectedError, result.Error);
                Assert.Null(result.Response);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(data.LocationId, persisted.LocationId);
                Assert.Equal(
                    data.ProfessionalId,
                    persisted.ProfessionalId);
                Assert.Equal(originalStart, persisted.StartsAt);
                Assert.Equal(
                    originalStart.AddMinutes(45),
                    persisted.EndsAt);
                Assert.Equal(AppointmentStatus.Pending, persisted.Status);
                Assert.Equal(CreatedAt, persisted.UpdatedAt);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    persisted.ReservationExpiresAt);
                Assert.Equal(65m, persisted.TotalPriceSnapshot);
                Assert.Equal(45, persisted.TotalDurationMinutesSnapshot);
                Assert.Single(persisted.Items);

                Assert.Equal(
                    0,
                    await context.IdempotencyRecords.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(599, false)]
    [InlineData(600, true)]
    [InlineData(601, true)]
    public async Task AppointmentReschedulingService_ShouldRespectReservationDeadline(
        int elapsedSeconds,
        bool shouldExpire)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var newStart = originalStart.AddHours(1);
        var now = CreatedAt.AddSeconds(elapsedSeconds);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        originalStart));

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                newStart,
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(command);

                Assert.Equal(!shouldExpire, result.IsSuccess);

                if (shouldExpire)
                {
                    Assert.Equal(
                        RescheduleAppointmentError.ReservationExpired,
                        result.Error);
                    Assert.Null(result.Response);
                }
                else
                {
                    var response = Assert.IsType<AppointmentResponse>(
                        result.Response);

                    Assert.Equal(newStart, response.StartsAt);
                    Assert.Equal(
                        CreatedAt.AddMinutes(10),
                        response.ReservationExpiresAt);
                }
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                var expectedStart = shouldExpire
                    ? originalStart
                    : newStart;

                Assert.Equal(expectedStart, persisted.StartsAt);
                Assert.Equal(
                    expectedStart.AddMinutes(45),
                    persisted.EndsAt);

                Assert.Equal(
                    shouldExpire
                        ? AppointmentStatus.Expired
                        : AppointmentStatus.Pending,
                    persisted.Status);

                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    persisted.ReservationExpiresAt);
                Assert.Equal(now, persisted.UpdatedAt);
                Assert.Equal(CreatedAt, persisted.CreatedAt);
                Assert.Equal(data.LocationId, persisted.LocationId);
                Assert.Equal(
                    data.ProfessionalId,
                    persisted.ProfessionalId);
                Assert.Equal(65m, persisted.TotalPriceSnapshot);
                Assert.Equal(45, persisted.TotalDurationMinutesSnapshot);
                Assert.Single(persisted.Items);
                Assert.Null(persisted.CancelledAt);
                Assert.Null(persisted.CancellationReason);

                Assert.Equal(
                    shouldExpire ? 0 : 1,
                    await context.IdempotencyRecords.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("appointment")]
    [InlineData("location")]
    [InlineData("professional")]
    [InlineData("start")]
    public async Task AppointmentReschedulingService_WithSameKeyAndDifferentContent_ShouldConflict(
        string scenario)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        var originalStart = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var newStart = originalStart.AddHours(1);
        var now = CreatedAt.AddMinutes(5);

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        originalStart));

                await context.SaveChangesAsync();
            }

            var command = new RescheduleAppointmentCommand(
                appointmentId,
                data.LocationId,
                data.ProfessionalId,
                newStart,
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now)));

                Assert.True(
                    (await handler.HandleAsync(command)).IsSuccess);
            }

            var conflictingCommand = scenario switch
            {
                "appointment" => command with
                {
                    AppointmentId = Guid.NewGuid()
                },
                "location" => command with
                {
                    LocationId = Guid.NewGuid()
                },
                "professional" => command with
                {
                    ProfessionalId = Guid.NewGuid()
                },
                "start" => command with
                {
                    StartsAt = newStart.AddMinutes(15)
                },
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new RescheduleAppointmentHandler(
                    new AppointmentReschedulingService(
                        context,
                        new FixedTimeProvider(now.AddMinutes(1))));

                var result = await handler.HandleAsync(
                    conflictingCommand);

                Assert.Equal(
                    RescheduleAppointmentError.IdempotencyConflict,
                    result.Error);
                Assert.Null(result.Response);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(data.LocationId, persisted.LocationId);
                Assert.Equal(
                    data.ProfessionalId,
                    persisted.ProfessionalId);
                Assert.Equal(newStart, persisted.StartsAt);
                Assert.Equal(newStart.AddMinutes(45), persisted.EndsAt);
                Assert.Equal(now, persisted.UpdatedAt);
                Assert.Equal(AppointmentStatus.Pending, persisted.Status);

                Assert.Equal(
                    1,
                    await context.IdempotencyRecords.CountAsync());
                Assert.Equal(
                    1,
                    await context.Appointments.CountAsync());
                Assert.Equal(
                    1,
                    await context.AppointmentItems.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(599, false)]
    [InlineData(600, true)]
    [InlineData(601, true)]
    public async Task AppointmentCancellationService_ShouldRespectReservationDeadline(
        int elapsedSeconds,
        bool shouldExpire)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var now = CreatedAt.AddSeconds(elapsedSeconds);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            var command = new CancelAppointmentCommand(
                appointmentId,
                AppointmentStatus.CancelledByClient,
                "Cliente solicitou",
                Guid.NewGuid().ToString());

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(now)));

                var result = await handler.HandleAsync(command);

                Assert.Equal(!shouldExpire, result.IsSuccess);

                if (shouldExpire)
                {
                    Assert.Equal(
                        CancelAppointmentError.ReservationExpired,
                        result.Error);
                    Assert.Null(result.Response);
                }
                else
                {
                    Assert.NotNull(result.Response);
                    Assert.Equal(now, result.Response.CancelledAt);
                }
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(
                    shouldExpire
                        ? AppointmentStatus.Expired
                        : AppointmentStatus.CancelledByClient,
                    appointment.Status);

                Assert.False(appointment.OccupiesSchedule);
                Assert.Equal(now, appointment.UpdatedAt);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    appointment.ReservationExpiresAt);

                if (shouldExpire)
                {
                    Assert.Null(appointment.CancellationReason);
                    Assert.Null(appointment.CancelledAt);
                }
                else
                {
                    Assert.Equal(
                        "Cliente solicitou",
                        appointment.CancellationReason);
                    Assert.Equal(now, appointment.CancelledAt);
                }

                Assert.Equal(65m, appointment.TotalPriceSnapshot);
                Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);
                Assert.Single(appointment.Items);

                Assert.Equal(
                    shouldExpire ? 0 : 1,
                    await context.IdempotencyRecords.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("origin")]
    [InlineData("appointment")]
    public async Task AppointmentCancellationService_WithSameKeyAndDifferentContent_ShouldConflict(
        string scenario)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var otherAppointmentId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();
        var cancelledAt = CreatedAt.AddMinutes(5);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        otherAppointmentId,
                        CreatedAt.AddHours(3)));

                await context.SaveChangesAsync();
            }

            var command = new CancelAppointmentCommand(
                appointmentId,
                AppointmentStatus.CancelledByClient,
                "Motivo original",
                key);

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(cancelledAt)));

                var result = await handler.HandleAsync(command);

                Assert.True(result.IsSuccess);
            }

            var conflictingCommand = scenario switch
            {
                "reason" => command with
                {
                    Reason = "Outro motivo"
                },
                "origin" => command with
                {
                    CancellationStatus =
                        AppointmentStatus.CancelledByBusiness
                },
                "appointment" => command with
                {
                    AppointmentId = otherAppointmentId
                },
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };

            await using (var context = CreateContext(data.TenantId))
            {
                var handler = new CancelAppointmentHandler(
                    new AppointmentCancellationService(
                        context,
                        new FixedTimeProvider(CreatedAt.AddMinutes(6))));

                var result = await handler.HandleAsync(
                    conflictingCommand);

                Assert.False(result.IsSuccess);
                Assert.Equal(
                    CancelAppointmentError.IdempotencyConflict,
                    result.Error);
                Assert.Null(result.Response);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointments = await context.Appointments
                    .AsNoTracking()
                    .ToListAsync();

                var cancelled = Assert.Single(
                    appointments,
                    entity => entity.Id == appointmentId);

                Assert.Equal(
                    AppointmentStatus.CancelledByClient,
                    cancelled.Status);
                Assert.Equal(
                    "Motivo original",
                    cancelled.CancellationReason);
                Assert.Equal(cancelledAt, cancelled.CancelledAt);
                Assert.Equal(cancelledAt, cancelled.UpdatedAt);

                var untouched = Assert.Single(
                    appointments,
                    entity => entity.Id == otherAppointmentId);

                Assert.Equal(
                    AppointmentStatus.Pending,
                    untouched.Status);
                Assert.Null(untouched.CancellationReason);
                Assert.Null(untouched.CancelledAt);
                Assert.Equal(CreatedAt, untouched.UpdatedAt);

                Assert.Equal(
                    1,
                    await context.IdempotencyRecords.CountAsync());
                Assert.Equal(
                    2,
                    await context.AppointmentItems.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("missing-reason")]
    [InlineData("blank-reason")]
    [InlineData("missing-date")]
    [InlineData("date-before-creation")]
    [InlineData("date-after-update")]
    [InlineData("reason-without-cancellation")]
    [InlineData("date-without-cancellation")]
    public async Task AppointmentCancellation_WithInvalidData_ShouldReject(
        string scenario)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            var status = "CANCELLED_BY_CLIENT";
            string? reason = "Motivo válido";
            DateTimeOffset? cancelledAt = CreatedAt.AddMinutes(5);
            var updatedAt = CreatedAt.AddMinutes(5);

            switch (scenario)
            {
                case "missing-reason":
                    reason = null;
                    break;

                case "blank-reason":
                    reason = "   ";
                    break;

                case "missing-date":
                    cancelledAt = null;
                    break;

                case "date-before-creation":
                    cancelledAt = CreatedAt.AddMinutes(-1);
                    break;

                case "date-after-update":
                    cancelledAt = updatedAt.AddMinutes(1);
                    break;

                case "reason-without-cancellation":
                    status = "PENDING";
                    cancelledAt = null;
                    break;

                case "date-without-cancellation":
                    status = "PENDING";
                    reason = null;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }

            await using (var context = CreateContext(data.TenantId))
            {
                // SQL direto exercita o CHECK sem passar pelo domínio.
                var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(
                    async () =>
                    {
                        await context.Database.ExecuteSqlInterpolatedAsync(
                            $"""
                            UPDATE appointments
                            SET status = {status},
                                cancellation_reason = {reason},
                                cancelled_at = {cancelledAt},
                                updated_at = {updatedAt}
                            WHERE tenant_id = {data.TenantId}
                              AND id = {appointmentId}
                            """);
                    });

                Assert.Equal("23514", exception.SqlState);
                Assert.Equal(
                    "CK_appointments_cancellation",
                    exception.ConstraintName);
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var persisted = await context.Appointments
                    .AsNoTracking()
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(AppointmentStatus.Pending, persisted.Status);
                Assert.Null(persisted.CancellationReason);
                Assert.Null(persisted.CancelledAt);
                Assert.Equal(CreatedAt, persisted.UpdatedAt);
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(AppointmentStatus.CancelledByClient)]
    [InlineData(AppointmentStatus.CancelledByBusiness)]
    public async Task AppointmentCancellation_ShouldPersistAuditAndReleaseInterval(
        AppointmentStatus cancellationStatus)
    {
        var data = new TestData();
        var appointmentId = Guid.NewGuid();
        var replacementId = Guid.NewGuid();

        var startsAt = CreatedAt.AddHours(2);
        var cancelledAt = CreatedAt.AddMinutes(5);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        startsAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.True(appointment.Cancel(
                    cancellationStatus,
                    "  Cancelamento solicitado  ",
                    cancelledAt));

                await context.SaveChangesAsync();
            }

            // Outro contexto confirma os valores efetivamente persistidos.
            await using (var context = CreateContext(data.TenantId))
            {
                var appointment = await context.Appointments
                    .AsNoTracking()
                    .Include(entity => entity.Items)
                    .SingleAsync(entity => entity.Id == appointmentId);

                Assert.Equal(cancellationStatus, appointment.Status);
                Assert.False(appointment.OccupiesSchedule);
                Assert.Equal(
                    "Cancelamento solicitado",
                    appointment.CancellationReason);
                Assert.Equal(cancelledAt, appointment.CancelledAt);
                Assert.Equal(cancelledAt, appointment.UpdatedAt);
                Assert.Equal(CreatedAt, appointment.CreatedAt);

                Assert.Equal(startsAt, appointment.StartsAt);
                Assert.Equal(startsAt.AddMinutes(45), appointment.EndsAt);
                Assert.Equal(65m, appointment.TotalPriceSnapshot);
                Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);
                Assert.Equal(
                    CreatedAt.AddMinutes(10),
                    appointment.ReservationExpiresAt);

                var item = Assert.Single(appointment.Items);

                Assert.Equal(data.ServiceId, item.ServiceId);
                Assert.Equal("Corte", item.ServiceNameSnapshot);
                Assert.Equal(65m, item.PriceSnapshot);
                Assert.Equal(45, item.DurationMinutesSnapshot);
            }

            // A exclusion constraint deve permitir ocupar o horário liberado.
            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        replacementId,
                        startsAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var appointments = await context.Appointments
                    .AsNoTracking()
                    .ToListAsync();

                Assert.Equal(2, appointments.Count);

                var replacement = Assert.Single(
                    appointments,
                    entity => entity.Id == replacementId);

                Assert.Equal(
                    AppointmentStatus.Pending,
                    replacement.Status);
                Assert.True(replacement.OccupiesSchedule);
                Assert.Null(replacement.CancellationReason);
                Assert.Null(replacement.CancelledAt);

                Assert.Equal(
                    2,
                    await context.AppointmentItems.CountAsync());
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("missing-customer", CreateAppointmentError.CustomerNotFound)]
    [InlineData("missing-location", CreateAppointmentError.LocationNotFound)]
    [InlineData("missing-professional", CreateAppointmentError.ProfessionalNotFound)]
    [InlineData("missing-service", CreateAppointmentError.ServiceNotFound)]
    [InlineData("inactive-service", CreateAppointmentError.InvalidServices)]
    [InlineData("not-offered", CreateAppointmentError.ServicesNotOffered)]
    [InlineData("not-eligible", CreateAppointmentError.ProfessionalNotEligible)]
    public async Task AppointmentCreation_WithUnavailableResource_ShouldNotPersist(
        string scenario,
        CreateAppointmentError expectedError)
    {
        var data = new TestData();

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                if (scenario == "inactive-service")
                {
                    var service = await context.Services
                        .SingleAsync(entity => entity.Id == data.ServiceId);

                    service.Update(
                        service.Name,
                        service.Price,
                        service.DurationMinutes,
                        false,
                        CreatedAt.AddMinutes(1));
                }
                else if (scenario == "not-offered")
                {
                    var link = await context.LocationServices
                        .SingleAsync(entity =>
                            entity.LocationId == data.LocationId &&
                            entity.ServiceId == data.ServiceId);

                    context.LocationServices.Remove(link);
                }
                else if (scenario == "not-eligible")
                {
                    var link = await context.ProfessionalServices
                        .SingleAsync(entity =>
                            entity.ProfessionalId == data.ProfessionalId &&
                            entity.ServiceId == data.ServiceId);

                    context.ProfessionalServices.Remove(link);
                }

                await context.SaveChangesAsync();
            }

            var command = new CreateAppointmentCommand(
                scenario == "missing-customer"
                    ? Guid.NewGuid()
                    : data.CustomerId,
                scenario == "missing-location"
                    ? Guid.NewGuid()
                    : data.LocationId,
                scenario == "missing-professional"
                    ? Guid.NewGuid()
                    : data.ProfessionalId,
                [
                    scenario == "missing-service"
                        ? Guid.NewGuid()
                        : data.ServiceId
                ],
                new DateTimeOffset(
                    2026, 10, 19, 12, 0, 0, TimeSpan.Zero),
                $"unavailable-resource-{scenario}");

            await using (var context = CreateContext(data.TenantId))
            {
                var result = await CreateHandler(context)
                    .HandleAsync(command);

                Assert.False(result.IsSuccess);
                Assert.Equal(expectedError, result.Error);
                Assert.Null(result.Response);
            }

            await using var readContext = CreateContext(data.TenantId);

            Assert.False(await readContext.Appointments.AnyAsync());
            Assert.False(await readContext.AppointmentItems.AnyAsync());
            Assert.False(await readContext.IdempotencyRecords.AnyAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("outside-window")]
    [InlineData("off-grid")]
    [InlineData("wrong-day")]
    [InlineData("past")]
    [InlineData("location-block")]
    [InlineData("professional-block")]
    [InlineData("occupied")]
    public async Task AppointmentCreation_WithUnavailableSlot_ShouldNotPersist(
        string scenario)
    {
        var data = new TestData();

        // Segunda-feira, 9h em São Paulo.
        var startsAt = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var requestedStart = scenario switch
        {
            "outside-window" => startsAt.AddHours(-1),
            "off-grid" => startsAt.AddMinutes(1),
            "wrong-day" => startsAt.AddDays(1),
            _ => startsAt
        };

        var command = new CreateAppointmentCommand(
            data.CustomerId,
            data.LocationId,
            data.ProfessionalId,
            [data.ServiceId],
            requestedStart,
            $"unavailable-slot-{scenario}");

        try
        {
            await SeedBookableAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                if (scenario is "location-block" or "professional-block")
                {
                    context.ScheduleBlocks.Add(
                        new ScheduleBlock(
                            Guid.NewGuid(),
                            data.TenantId,
                            data.LocationId,
                            scenario == "professional-block"
                                ? data.ProfessionalId
                                : (Guid?)null,
                            startsAt.AddMinutes(10),
                            startsAt.AddMinutes(20),
                            CreatedAt));
                }

                if (scenario == "occupied")
                {
                    context.Appointments.Add(
                        CreateAppointment(
                            data,
                            Guid.NewGuid(),
                            startsAt));
                }

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                var now = scenario == "past"
                    ? startsAt.AddMinutes(1)
                    : CreatedAt;

                var result = await CreateHandler(context, now)
                    .HandleAsync(command);

                Assert.False(result.IsSuccess);
                Assert.Equal(
                    CreateAppointmentError.SlotUnavailable,
                    result.Error);
                Assert.Null(result.Response);
            }

            await using var readContext = CreateContext(data.TenantId);

            var expectedCount = scenario == "occupied" ? 1 : 0;

            Assert.Equal(
                expectedCount,
                await readContext.Appointments.CountAsync());

            Assert.Equal(
                expectedCount,
                await readContext.AppointmentItems.CountAsync());

            Assert.False(
                await readContext.IdempotencyRecords.AnyAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("location")]
    [InlineData("professional")]
    [InlineData("service")]
    [InlineData("start")]
    public async Task AppointmentCreation_WithSameKeyAndDifferentContent_ShouldConflict(
        string changedField)
    {
        var data = new TestData();

        var command = new CreateAppointmentCommand(
            data.CustomerId,
            data.LocationId,
            data.ProfessionalId,
            [data.ServiceId],
            new DateTimeOffset(
                2026, 10, 19, 12, 0, 0, TimeSpan.Zero),
            "appointment-content-conflict");

        try
        {
            await SeedBookableAsync(data);

            AppointmentResponse original;

            await using (var context = CreateContext(data.TenantId))
            {
                var result = await CreateHandler(context)
                    .HandleAsync(command);

                Assert.True(result.IsSuccess);
                Assert.NotNull(result.Response);

                original = result.Response;
            }

            var changedCommand = changedField switch
            {
                "customer" => command with
                {
                    CustomerId = Guid.NewGuid()
                },
                "location" => command with
                {
                    LocationId = Guid.NewGuid()
                },
                "professional" => command with
                {
                    ProfessionalId = Guid.NewGuid()
                },
                "service" => command with
                {
                    ServiceIds = [Guid.NewGuid()]
                },
                "start" => command with
                {
                    StartsAt = command.StartsAt.AddMinutes(15)
                },
                _ => throw new ArgumentOutOfRangeException(
                    nameof(changedField))
            };

            await using (var context = CreateContext(data.TenantId))
            {
                var result = await CreateHandler(context)
                    .HandleAsync(changedCommand);

                Assert.False(result.IsSuccess);
                Assert.Equal(
                    CreateAppointmentError.IdempotencyConflict,
                    result.Error);
                Assert.Null(result.Response);
            }

            await using var readContext = CreateContext(data.TenantId);

            var persisted = await readContext.Appointments
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(original.Id, persisted.Id);
            Assert.Equal(original.StartsAt, persisted.StartsAt);
            Assert.Equal(AppointmentStatus.Pending, persisted.Status);
            Assert.Equal(CreatedAt, persisted.UpdatedAt);

            Assert.Equal(
                1,
                await readContext.AppointmentItems.CountAsync());

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(command.IdempotencyKey, record.Key);
            Assert.Equal(
                AppointmentRequestHasher.Compute(command),
                record.RequestHash);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending, true)]
    [InlineData(AppointmentStatus.Confirmed, true)]
    [InlineData(AppointmentStatus.ConfirmedByClient, true)]
    [InlineData(AppointmentStatus.RescheduleRequested, true)]
    [InlineData(AppointmentStatus.CancelledByClient, false)]
    [InlineData(AppointmentStatus.CancelledByBusiness, false)]
    [InlineData(AppointmentStatus.Completed, false)]
    [InlineData(AppointmentStatus.NoShow, false)]
    [InlineData(AppointmentStatus.Expired, false)]
    public async Task Appointment_ShouldApplyOverlapProtectionByStatus(
        AppointmentStatus status,
        bool shouldConflict)
    {
        var data = new TestData();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var startsAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                var first = CreateAppointment(data, firstId, startsAt);

                context.Appointments.Add(first);

                if (status is AppointmentStatus.CancelledByClient
                    or AppointmentStatus.CancelledByBusiness)
                {
                    first.Cancel(
                        status,
                        "Cancelamento para teste de sobreposição",
                        CreatedAt.AddMinutes(5));
                }
                else
                {
                    // Prepara os demais estados para testar a constraint.
                    context.Entry(first)
                        .Property(entity => entity.Status)
                        .CurrentValue = status;
                }

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(data, secondId, startsAt));

                if (shouldConflict)
                {
                    var exception =
                        await Assert.ThrowsAsync<DbUpdateException>(
                            () => context.SaveChangesAsync());

                    var postgresException =
                        Assert.IsType<Npgsql.PostgresException>(
                            exception.InnerException);

                    Assert.Equal(
                        Npgsql.PostgresErrorCodes.ExclusionViolation,
                        postgresException.SqlState);

                    Assert.Equal(
                        "ex_appointments_no_overlap",
                        postgresException.ConstraintName);
                }
                else
                {
                    await context.SaveChangesAsync();
                }
            }

            await using var readContext = CreateContext(data.TenantId);

            var persisted = await readContext.Appointments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == firstId);

            Assert.Equal(status, persisted.Status);
            Assert.Equal(shouldConflict, persisted.OccupiesSchedule);

            Assert.Equal(
                !shouldConflict,
                await readContext.Appointments
                    .AnyAsync(entity => entity.Id == secondId));

            Assert.Equal(
                shouldConflict ? 1 : 2,
                await readContext.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(
        "business",
        "FK_appointments_businesses_tenant_id_business_id")]
    [InlineData(
        "customer",
        "FK_appointments_customers_tenant_id_customer_id")]
    [InlineData(
        "professional-location",
        "FK_appointments_professional_locations_tenant_id_professional_~")]
    public async Task Appointment_WithCrossTenantReference_ShouldReject(
        string reference,
        string expectedConstraint)
    {
        var dataA = new TestData();
        var dataB = new TestData();
        var appointmentId = Guid.NewGuid();

        try
        {
            await SeedAsync(dataA);
            await SeedAsync(dataB);

            await using (var context = CreateContext(dataB.TenantId))
            {
                context.Appointments.Add(
                    new Appointment(
                        appointmentId,
                        dataB.TenantId,
                        reference == "business"
                            ? dataA.BusinessId
                            : dataB.BusinessId,
                        reference == "professional-location"
                            ? dataA.LocationId
                            : dataB.LocationId,
                        reference == "customer"
                            ? dataA.CustomerId
                            : dataB.CustomerId,
                        reference == "professional-location"
                            ? dataA.ProfessionalId
                            : dataB.ProfessionalId,
                        CreatedAt.AddHours(2),
                        CreatedAt,
                        TimeSpan.FromMinutes(10),
                        [
                            new AppointmentItem(
                                Guid.NewGuid(),
                                dataB.TenantId,
                                appointmentId,
                                dataB.ServiceId,
                                "Corte",
                                65m,
                                45)
                        ]));

                var exception =
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync());

                var postgresException =
                    Assert.IsType<Npgsql.PostgresException>(
                        exception.InnerException);

                Assert.Equal(
                    Npgsql.PostgresErrorCodes.ForeignKeyViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    expectedConstraint,
                    postgresException.ConstraintName);
            }

            await using var verificationContext =
                CreateContext(dataB.TenantId);

            Assert.False(
                await verificationContext.Appointments
                    .IgnoreQueryFilters()
                    .AnyAsync(entity => entity.Id == appointmentId));

            Assert.False(
                await verificationContext.AppointmentItems
                    .IgnoreQueryFilters()
                    .AnyAsync(item =>
                        item.AppointmentId == appointmentId));
        }
        finally
        {
            await CleanupAsync(dataA.TenantId);
            await CleanupAsync(dataB.TenantId);
        }
    }

    [Theory]
    [InlineData(
        "appointment",
        "FK_appointment_items_appointments_tenant_id_appointment_id")]
    [InlineData(
        "service",
        "FK_appointment_items_services_tenant_id_service_id")]
    public async Task AppointmentItem_WithCrossTenantReference_ShouldReject(
        string reference,
        string expectedConstraint)
    {
        var dataA = new TestData();
        var dataB = new TestData();
        var appointmentAId = Guid.NewGuid();
        var appointmentBId = Guid.NewGuid();
        var attemptedItemId = Guid.NewGuid();

        try
        {
            await SeedAsync(dataA);
            await SeedAsync(dataB);

            await using (var context = CreateContext(dataA.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        dataA,
                        appointmentAId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(dataB.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        dataB,
                        appointmentBId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(dataB.TenantId))
            {
                context.AppointmentItems.Add(
                    new AppointmentItem(
                        attemptedItemId,
                        dataB.TenantId,
                        reference == "appointment"
                            ? appointmentAId
                            : appointmentBId,
                        reference == "service"
                            ? dataA.ServiceId
                            : dataB.ServiceId,
                        "Invalid Reference",
                        10m,
                        10));

                var exception =
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync());

                var postgresException =
                    Assert.IsType<Npgsql.PostgresException>(
                        exception.InnerException);

                Assert.Equal(
                    Npgsql.PostgresErrorCodes.ForeignKeyViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    expectedConstraint,
                    postgresException.ConstraintName);
            }

            await using var verificationContext =
                CreateContext(dataB.TenantId);

            Assert.False(
                await verificationContext.AppointmentItems
                    .IgnoreQueryFilters()
                    .AnyAsync(item => item.Id == attemptedItemId));

            Assert.Equal(
                2,
                await verificationContext.AppointmentItems
                    .IgnoreQueryFilters()
                    .CountAsync(item =>
                        item.AppointmentId == appointmentAId ||
                        item.AppointmentId == appointmentBId));
        }
        finally
        {
            await CleanupAsync(dataA.TenantId);
            await CleanupAsync(dataB.TenantId);
        }
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task AppointmentItem_ShouldRejectCrossTenantWrites(
        string operation)
    {
        var data = new TestData();
        var otherTenantId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var attemptedItemId = Guid.NewGuid();

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        appointmentId,
                        CreatedAt.AddHours(2)));

                await context.SaveChangesAsync();
            }

            await using (var otherContext = CreateContext(otherTenantId))
            {
                if (operation == "insert")
                {
                    otherContext.AppointmentItems.Add(
                        new AppointmentItem(
                            attemptedItemId,
                            data.TenantId,
                            appointmentId,
                            data.ServiceId,
                            "Attempted Item",
                            1m,
                            1));
                }
                else
                {
                    // Bypass intencional para testar a proteção de escrita.
                    var item = await otherContext.AppointmentItems
                        .IgnoreQueryFilters()
                        .SingleAsync(entity =>
                            entity.AppointmentId == appointmentId);

                    if (operation == "update")
                    {
                        // Simula alteração fora da API pública do domínio.
                        otherContext.Entry(item)
                            .Property(entity => entity.PriceSnapshot)
                            .CurrentValue = 1m;
                    }
                    else
                    {
                        otherContext.AppointmentItems.Remove(item);
                    }
                }

                var exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => otherContext.SaveChangesAsync());

                Assert.Equal(
                    "Cross-tenant data modification is not allowed.",
                    exception.Message);
            }

            await using var verificationContext =
                CreateContext(data.TenantId);

            var persisted = await verificationContext.AppointmentItems
                .AsNoTracking()
                .SingleAsync(item => item.AppointmentId == appointmentId);

            Assert.NotEqual(attemptedItemId, persisted.Id);
            Assert.Equal(data.TenantId, persisted.TenantId);
            Assert.Equal(data.ServiceId, persisted.ServiceId);
            Assert.Equal("Corte", persisted.ServiceNameSnapshot);
            Assert.Equal(65m, persisted.PriceSnapshot);
            Assert.Equal(45, persisted.DurationMinutesSnapshot);

            var appointment = await verificationContext.Appointments
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == appointmentId);

            Assert.Equal(65m, appointment.TotalPriceSnapshot);
            Assert.Equal(AppointmentStatus.Pending, appointment.Status);
            Assert.Equal(CreatedAt, appointment.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task Appointment_ShouldRejectCrossTenantWrites(
        string operation)
    {
        var data = new TestData();
        var otherTenantId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var startsAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(data);

            if (operation != "insert")
            {
                await using var context = CreateContext(data.TenantId);

                context.Appointments.Add(
                    CreateAppointment(data, appointmentId, startsAt));

                await context.SaveChangesAsync();
            }

            await using (var otherContext = CreateContext(otherTenantId))
            {
                if (operation == "insert")
                {
                    otherContext.Appointments.Add(
                        CreateAppointment(data, appointmentId, startsAt));
                }
                else
                {
                    // Bypass intencional para testar a proteção de escrita.
                    var appointment = await otherContext.Appointments
                        .IgnoreQueryFilters()
                        .SingleAsync(entity => entity.Id == appointmentId);

                    if (operation == "update")
                    {
                        Assert.True(
                            appointment.Expire(CreatedAt.AddMinutes(10)));
                    }
                    else
                    {
                        otherContext.Appointments.Remove(appointment);
                    }
                }

                var exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => otherContext.SaveChangesAsync());

                Assert.Equal(
                    "Cross-tenant data modification is not allowed.",
                    exception.Message);
            }

            await using var verificationContext =
                CreateContext(data.TenantId);

            var persisted = await verificationContext.Appointments
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.Id == appointmentId);

            if (operation == "insert")
            {
                Assert.Null(persisted);

                Assert.False(
                    await verificationContext.AppointmentItems
                        .AnyAsync(item =>
                            item.AppointmentId == appointmentId));
            }
            else
            {
                Assert.NotNull(persisted);
                Assert.Equal(AppointmentStatus.Pending, persisted.Status);
                Assert.Equal(CreatedAt, persisted.CreatedAt);
                Assert.Equal(CreatedAt, persisted.UpdatedAt);
                Assert.Equal(startsAt, persisted.StartsAt);
                Assert.Equal(65m, persisted.TotalPriceSnapshot);

                Assert.Equal(
                    1,
                    await verificationContext.AppointmentItems
                        .CountAsync(item =>
                            item.AppointmentId == appointmentId));
            }
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(-30, true)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(-45, false)]
    [InlineData(45, false)]
    public async Task Appointment_ShouldRejectOverlapAndAllowAdjacentIntervals(
        int secondStartOffsetMinutes,
        bool shouldConflict)
    {
        var data = new TestData();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstStart = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(data);

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(data, firstId, firstStart));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(data.TenantId))
            {
                context.Appointments.Add(
                    CreateAppointment(
                        data,
                        secondId,
                        firstStart.AddMinutes(secondStartOffsetMinutes)));

                if (shouldConflict)
                {
                    var exception =
                        await Assert.ThrowsAsync<DbUpdateException>(
                            () => context.SaveChangesAsync());

                    var postgresException =
                        Assert.IsType<Npgsql.PostgresException>(
                            exception.InnerException);

                    Assert.Equal(
                        Npgsql.PostgresErrorCodes.ExclusionViolation,
                        postgresException.SqlState);

                    Assert.Equal(
                        "ex_appointments_no_overlap",
                        postgresException.ConstraintName);
                }
                else
                {
                    await context.SaveChangesAsync();
                }
            }

            await using var verificationContext =
                CreateContext(data.TenantId);

            Assert.True(
                await verificationContext.Appointments
                    .AnyAsync(entity => entity.Id == firstId));

            Assert.Equal(
                !shouldConflict,
                await verificationContext.Appointments
                    .AnyAsync(entity => entity.Id == secondId));

            Assert.Equal(
                shouldConflict ? 1 : 2,
                await verificationContext.Appointments.CountAsync());

            // A tentativa rejeitada também não deixa seu item persistido.
            Assert.Equal(
                shouldConflict ? 1 : 2,
                await verificationContext.AppointmentItems.CountAsync());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    #endregion

    #region Helpers

    private sealed class FailAfterPaymentCompletionSaveInterceptor
        : SaveChangesInterceptor
    {
        public int? SavedEntriesCount { get; private set; }
        public bool HadActiveTransaction { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            var context = eventData.Context;

            if (context is null)
            {
                return new ValueTask<int>(result);
            }

            var hasFinalResult = context.ChangeTracker
                .Entries<VirtualEmployee.Infrastructure.Persistence.Idempotency.IdempotencyRecord>()
                .Any(entry =>
                    entry.Entity.Operation ==
                        PaymentAttemptCompletionService.Operation);

            if (!hasFinalResult)
            {
                return new ValueTask<int>(result);
            }

            SavedEntriesCount = result;
            HadActiveTransaction =
                context.Database.CurrentTransaction is not null;

            throw new InvalidOperationException(
                "Simulated failure before payment completion commit.");
        }
    }
    private sealed class MutablePaymentTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }

        public MutablePaymentTimeProvider(DateTimeOffset utcNow)
        {
            UtcNow = utcNow.ToUniversalTime();
        }

        public override DateTimeOffset GetUtcNow()
        {
            return UtcNow.ToUniversalTime();
        }
    }

    private sealed class FailAfterPaymentPreparationSaveInterceptor
        : SaveChangesInterceptor
    {
        public int? SavedEntriesCount { get; private set; }
        public bool HadActiveTransaction { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            SavedEntriesCount = result;

            HadActiveTransaction =
                eventData.Context?.Database.CurrentTransaction is not null;

            throw new InvalidOperationException(
                "Simulated failure before payment preparation commit.");
        }
    }

    private sealed class BeforeCancellationSaveInterceptor(
        Func<Task> beforeSave)
        : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private bool _executed;

        public override async ValueTask<
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>>
            SavingChangesAsync(
                Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            if (!_executed)
            {
                _executed = true;
                await beforeSave();
            }

            return result;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static CreateAppointmentHandler CreateHandler(
        AppDbContext context,
        DateTimeOffset? now = null,
        int lifetimeMinutes = 10)
    {
        var service = new AppointmentCreationService(
            context,
            Options.Create(new AppointmentReservationOptions
            {
                LifetimeMinutes = lifetimeMinutes
            }),
            new FixedTimeProvider(now ?? CreatedAt));

        return new CreateAppointmentHandler(service);
    }

    private async Task SeedBookableAsync(TestData data)
    {
        await SeedAsync(data);

        await using var context = CreateContext(data.TenantId);

        context.LocationServices.Add(
            new LocationService(
                data.TenantId,
                data.LocationId,
                data.ServiceId,
                CreatedAt));

        context.ProfessionalServices.Add(
            new ProfessionalService(
                data.TenantId,
                data.ProfessionalId,
                data.ServiceId,
                CreatedAt));

        context.AvailabilityRules.Add(
            new AvailabilityRule(
                Guid.NewGuid(),
                data.TenantId,
                data.LocationId,
                data.ProfessionalId,
                DayOfWeek.Monday,
                new TimeOnly(9, 0),
                new TimeOnly(18, 0),
                CreatedAt));

        await context.SaveChangesAsync();
    }

    private sealed class ConcurrentSaveGate
        : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private readonly TaskCompletionSource<bool> _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _arrivals;

        public int Arrivals => Volatile.Read(ref _arrivals);

        public override async ValueTask<
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>>
            SavingChangesAsync(
                Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
            {
                _ready.TrySetResult(true);
            }

            await _ready.Task.WaitAsync(
                TimeSpan.FromSeconds(30),
                cancellationToken);

            return result;
        }
    }
    private AppDbContext CreateContext(
        Guid tenantId,
        Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor?
            interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString);

        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        var tenantContext = new TenantContext();
        tenantContext.Initialize(tenantId);

        return new AppDbContext(options.Options, tenantContext);
    }

    private static Appointment CreateAppointment(
        TestData data,
        Guid appointmentId,
        DateTimeOffset startsAt,
        int durationMinutes = 45)
    {
        return new Appointment(
            appointmentId,
            data.TenantId,
            data.BusinessId,
            data.LocationId,
            data.CustomerId,
            data.ProfessionalId,
            startsAt,
            CreatedAt,
            TimeSpan.FromMinutes(10),
            [
                new AppointmentItem(
                    Guid.NewGuid(),
                    data.TenantId,
                    appointmentId,
                    data.ServiceId,
                    "Corte",
                    65m,
                    durationMinutes)
            ]);
    }
    private sealed class TestData
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid BusinessId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        public Guid CustomerId { get; } = Guid.NewGuid();
        public Guid ProfessionalId { get; } = Guid.NewGuid();
        public Guid ServiceId { get; } = Guid.NewGuid();
    }

    private async Task SeedAsync(TestData data)
    {
        await using var context = CreateContext(data.TenantId);

        context.Tenants.Add(
            new Tenant(data.TenantId, "Appointment Tenant"));

        context.Businesses.Add(
            new Business(
                data.BusinessId,
                data.TenantId,
                BusinessTypeIds.Barbershop,
                "Appointment Business",
                CreatedAt));

        await context.SaveChangesAsync();

        context.Locations.Add(
            new Location(
                data.LocationId,
                data.TenantId,
                data.BusinessId,
                "Location",
                "BR",
                "America/Sao_Paulo",
                CreatedAt));

        context.Customers.Add(
            new Customer(
                data.CustomerId,
                data.TenantId,
                data.BusinessId,
                "+5511999990001",
                CreatedAt,
                "Customer"));

        context.Professionals.Add(
            new Professional(
                data.ProfessionalId,
                data.TenantId,
                data.BusinessId,
                "Professional",
                CreatedAt));

        context.Services.Add(
            new Service(
                data.ServiceId,
                data.TenantId,
                data.BusinessId,
                "Corte",
                ServiceType.Single,
                65m,
                45,
                CreatedAt));

        await context.SaveChangesAsync();

        context.ProfessionalLocations.Add(
            new ProfessionalLocation(
                data.TenantId,
                data.ProfessionalId,
                data.LocationId,
                CreatedAt));

        await context.SaveChangesAsync();
    }

    private async Task CleanupAsync(Guid tenantId)
    {
        await using var context = CreateContext(tenantId);

        // Somente limpeza dos dados de teste:
        // desfaz a referência circular antes de excluir as tabelas financeiras.
        await context.Payments
            .Where(payment =>
                payment.ConfirmedPaymentAttemptId != null ||
                payment.ConfirmedAt != null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    payment => payment.Status,
                    PaymentStatus.Pending)
                .SetProperty(
                    payment => payment.ConfirmedPaymentAttemptId,
                    (Guid?)null)
                .SetProperty(
                    payment => payment.ConfirmedAt,
                    (DateTimeOffset?)null));

        await context.PaymentAttempts.ExecuteDeleteAsync();
        await context.Payments.ExecuteDeleteAsync();
        await context.AppointmentHistoryEntries.ExecuteDeleteAsync();

        context.AppointmentItems.RemoveRange(
            await context.AppointmentItems.ToListAsync());
        await context.SaveChangesAsync();

        context.Appointments.RemoveRange(
            await context.Appointments.ToListAsync());
        await context.SaveChangesAsync();

        context.ScheduleBlocks.RemoveRange(
            await context.ScheduleBlocks.ToListAsync());

        context.AvailabilityRules.RemoveRange(
            await context.AvailabilityRules.ToListAsync());

        context.ProfessionalServices.RemoveRange(
            await context.ProfessionalServices.ToListAsync());

        context.LocationServices.RemoveRange(
            await context.LocationServices.ToListAsync());

        await context.SaveChangesAsync();

        context.ProfessionalLocations.RemoveRange(
            await context.ProfessionalLocations.ToListAsync());
        await context.SaveChangesAsync();

        context.Customers.RemoveRange(
            await context.Customers.ToListAsync());

        context.Services.RemoveRange(
            await context.Services.ToListAsync());

        context.Professionals.RemoveRange(
            await context.Professionals.ToListAsync());

        context.Locations.RemoveRange(
            await context.Locations.ToListAsync());

        await context.SaveChangesAsync();

        context.Businesses.RemoveRange(
            await context.Businesses.ToListAsync());

        context.IdempotencyRecords.RemoveRange(
            await context.IdempotencyRecords.ToListAsync());

        await context.SaveChangesAsync();

        var tenant = await context.Tenants
            .SingleOrDefaultAsync(entity => entity.Id == tenantId);

        if (tenant is not null)
        {
            context.Tenants.Remove(tenant);
            await context.SaveChangesAsync();
        }
    }

    #endregion
}
