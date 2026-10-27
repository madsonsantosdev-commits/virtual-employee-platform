using VirtualEmployee.Application.Payments.CreatePaymentAttempt;
using VirtualEmployee.Domain.Payments;
using Xunit;

namespace VirtualEmployee.Application.Tests.Payments;

public sealed class CreatePaymentAttemptHandlerTests
{
    #region Facts

    [Fact]
    public void Constructor_WithNullService_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CreatePaymentAttemptHandler(null!));
    }

    [Fact]
    public async Task HandleAsync_WithNullCommand_ShouldThrow()
    {
        var service = new RecordingService(CreateSuccess());
        var handler = new CreatePaymentAttemptHandler(service);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => handler.HandleAsync(null!));

        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task HandleAsync_WithEmptyAppointmentId_ShouldRejectBeforeService()
    {
        var service = new RecordingService(CreateSuccess());
        var handler = new CreatePaymentAttemptHandler(service);

        var result = await handler.HandleAsync(
            new CreatePaymentAttemptCommand(
                Guid.Empty,
                PaymentMethod.Pix,
                "valid-key"));

        Assert.False(result.IsSuccess);
        Assert.Equal(CreatePaymentAttemptError.InvalidRequest, result.Error);
        Assert.Null(result.Response);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task HandleAsync_ShouldForwardExactCommandAndCancellationToken()
    {
        var expected = CreateSuccess();
        var service = new RecordingService(expected);
        var handler = new CreatePaymentAttemptHandler(service);
        using var cancellation = new CancellationTokenSource();

        var command = new CreatePaymentAttemptCommand(
            Guid.NewGuid(),
            PaymentMethod.Pix,
            "Key-AbC_123");

        var result = await handler.HandleAsync(
            command,
            cancellation.Token);

        Assert.Same(expected, result);
        Assert.Same(command, service.Command);
        Assert.Equal(cancellation.Token, service.CancellationToken);
        Assert.Equal("Key-AbC_123", service.Command!.IdempotencyKey);
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task HandleAsync_WithMaximumKeyLength_ShouldSucceed()
    {
        var service = new RecordingService(CreateSuccess());
        var handler = new CreatePaymentAttemptHandler(service);
        var key = new string('k', 200);

        var result = await handler.HandleAsync(
            new CreatePaymentAttemptCommand(
                Guid.NewGuid(),
                PaymentMethod.Pix,
                key));

        Assert.True(result.IsSuccess);
        Assert.Equal(key, service.Command!.IdempotencyKey);
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task HandleAsync_WithOversizedKey_ShouldRejectBeforeService()
    {
        var service = new RecordingService(CreateSuccess());
        var handler = new CreatePaymentAttemptHandler(service);

        var result = await handler.HandleAsync(
            new CreatePaymentAttemptCommand(
                Guid.NewGuid(),
                PaymentMethod.Pix,
                new string('k', 201)));

        Assert.Equal(CreatePaymentAttemptError.InvalidRequest, result.Error);
        Assert.Null(result.Response);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task HandleAsync_ShouldPropagateCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var service = new RecordingService(CreateSuccess())
        {
            HonorCancellation = true
        };

        var handler = new CreatePaymentAttemptHandler(service);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.HandleAsync(
                new CreatePaymentAttemptCommand(
                    Guid.NewGuid(),
                    PaymentMethod.Pix,
                    "valid-key"),
                cancellation.Token));

        Assert.Equal(1, service.Calls);
        Assert.Equal(cancellation.Token, service.CancellationToken);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData(PaymentMethod.Pix)]
    [InlineData(PaymentMethod.CreditCard)]
    [InlineData(PaymentMethod.DebitCard)]
    public async Task HandleAsync_WithSupportedMethod_ShouldCallService(
        PaymentMethod method)
    {
        var service = new RecordingService(CreateSuccess());
        var handler = new CreatePaymentAttemptHandler(service);

        var result = await handler.HandleAsync(
            new CreatePaymentAttemptCommand(
                Guid.NewGuid(),
                method,
                "valid-key"));

        Assert.True(result.IsSuccess);
        Assert.Equal(method, service.Command!.PaymentMethod);
        Assert.Equal(1, service.Calls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(999)]
    public async Task HandleAsync_WithInvalidMethod_ShouldRejectBeforeService(
        int method)
    {
        var service = new RecordingService(CreateSuccess());
        var handler = new CreatePaymentAttemptHandler(service);

        var result = await handler.HandleAsync(
            new CreatePaymentAttemptCommand(
                Guid.NewGuid(),
                (PaymentMethod)method,
                "valid-key"));

        Assert.Equal(CreatePaymentAttemptError.InvalidRequest, result.Error);
        Assert.Null(result.Response);
        Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" key")]
    [InlineData("key ")]
    [InlineData("key value")]
    [InlineData("key\tvalue")]
    [InlineData("key\nvalue")]
    [InlineData("key\u0000value")]
    [InlineData("key\u00A0value")]
    public async Task HandleAsync_WithInvalidKey_ShouldRejectBeforeService(
        string? key)
    {
        var service = new RecordingService(CreateSuccess());
        var handler = new CreatePaymentAttemptHandler(service);

        var result = await handler.HandleAsync(
            new CreatePaymentAttemptCommand(
                Guid.NewGuid(),
                PaymentMethod.Pix,
                key!));

        Assert.False(result.IsSuccess);
        Assert.Equal(CreatePaymentAttemptError.InvalidRequest, result.Error);
        Assert.Null(result.Response);
        Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData(CreatePaymentAttemptError.AppointmentNotFound)]
    [InlineData(CreatePaymentAttemptError.InvalidAppointmentState)]
    [InlineData(CreatePaymentAttemptError.ReservationExpired)]
    [InlineData(CreatePaymentAttemptError.PaymentAlreadyConfirmed)]
    [InlineData(CreatePaymentAttemptError.IdempotencyConflict)]
    [InlineData(CreatePaymentAttemptError.ConcurrencyConflict)]
    [InlineData(CreatePaymentAttemptError.ProviderError)]
    public async Task HandleAsync_ShouldPreserveServiceFailure(
        CreatePaymentAttemptError error)
    {
        var expected = CreatePaymentAttemptResult.Failure(error);
        var service = new RecordingService(expected);
        var handler = new CreatePaymentAttemptHandler(service);

        var result = await handler.HandleAsync(
            new CreatePaymentAttemptCommand(
                Guid.NewGuid(),
                PaymentMethod.Pix,
                "valid-key"));

        Assert.Same(expected, result);
        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.Error);
        Assert.Null(result.Response);
        Assert.Equal(1, service.Calls);
    }

    #endregion

    #region Helpers

    private static CreatePaymentAttemptResult CreateSuccess()
    {
        return CreatePaymentAttemptResult.Success(
            new CreatePaymentAttemptResponse(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "PIX",
                "PENDING",
                65m,
                "https://checkout.example.test/payment",
                new DateTimeOffset(
                    2026, 10, 27, 12, 10, 0,
                    TimeSpan.Zero)));
    }

    private sealed class RecordingService : ICreatePaymentAttemptService
    {
        private readonly CreatePaymentAttemptResult _result;

        public int Calls { get; private set; }
        public CreatePaymentAttemptCommand? Command { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public bool HonorCancellation { get; init; }

        public RecordingService(CreatePaymentAttemptResult result)
        {
            _result = result;
        }

        public Task<CreatePaymentAttemptResult> CreateAsync(
            CreatePaymentAttemptCommand command,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Command = command;
            CancellationToken = cancellationToken;

            if (HonorCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Task.FromResult(_result);
        }
    }

    #endregion
}