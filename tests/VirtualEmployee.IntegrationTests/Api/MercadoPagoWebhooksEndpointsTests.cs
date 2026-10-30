using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Customers;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Payments;
using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class MercadoPagoWebhooksEndpointsTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private const string Secret = "webhook-http-test-secret";
    private const string RequestId = "11111111-2222-4333-8444-555555555555";
    private const string Timestamp = "1793217600";
    private readonly PostgreSqlFixture _fixture;

    public MercadoPagoWebhooksEndpointsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    #region Facts

    [Fact]
    public async Task Webhook_WhenDisabled_ShouldNotExposeRoute()
    {
        var data = new TestData();
        var state = new ProviderState(data);
        using var factory = CreateFactory(data, state, webhooksEnabled: false);
        using var client = CreateClient(factory);
        using var request = CreateRequest(data);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(state.Paths);
    }

    [Fact]
    public async Task Webhook_ShouldPersistAndReplayWithoutTenantHeader()
    {
        var data = new TestData();
        var state = new ProviderState(data);
        try
        {
            await SeedAsync(data);
            using var factory = CreateFactory(data, state);
            using var client = CreateClient(factory);
            using var firstRequest = CreateRequest(data);
            using var first = await client.SendAsync(firstRequest);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            // The configured webhook account, not this header, owns the tenant.
            using var replayRequest = CreateRequest(data);
            replayRequest.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());
            using var replay = await client.SendAsync(replayRequest);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

            await using var context = CreateContext(data.TenantId);
            var appointment = await context.Appointments.SingleAsync();
            var payment = await context.Payments.SingleAsync();
            var attempt = await context.PaymentAttempts.SingleAsync();
            Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
            Assert.Equal(PaymentStatus.Confirmed, payment.Status);
            Assert.Equal(PaymentAttemptStatus.Confirmed, attempt.Status);
            Assert.Equal(data.AttemptId, payment.ConfirmedPaymentAttemptId);
            Assert.Equal(data.OrderId, attempt.ProviderPaymentId);
            var history = await context.AppointmentHistoryEntries.SingleAsync();
            Assert.Equal(AppointmentHistoryType.PaymentConfirmed, history.Type);
            Assert.Equal(1, await context.IdempotencyRecords.CountAsync(
                x => x.Operation == PaymentConfirmationService.Operation));
            Assert.Equal(4, state.Paths.Count);
            Assert.All(state.Tokens, x => Assert.Equal("http-test-token", x));
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Fact]
    public async Task Webhook_WithAnotherTenantAccount_ShouldNotAccessOwnerAttempt()
    {
        var owner = new TestData();
        var receiver = new TestData();
        var state = new ProviderState(owner);
        try
        {
            await SeedAsync(owner);
            using var factory = CreateFactory(receiver, state);
            using var client = CreateClient(factory);
            using var request = CreateRequest(receiver, orderId: owner.OrderId);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Empty(state.Paths);
            await AssertPendingAsync(owner);
        }
        finally
        {
            await CleanupAsync(owner.TenantId);
        }
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData("missing-signature")]
    [InlineData("invalid-signature")]
    [InlineData("missing-request")]
    [InlineData("missing-data")]
    [InlineData("changed-data")]
    [InlineData("unknown-webhook")]
    [InlineData("duplicate-data")]
    [InlineData("wrong-secret")]
    public async Task Webhook_WithInvalidAuthentication_ShouldRejectBeforeProviderCall(
        string scenario)
    {
        var data = new TestData();
        var state = new ProviderState(data);
        using var factory = CreateFactory(data, state);
        using var client = CreateClient(factory);
        using var request = CreateRequest(data, scenario);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(state.Paths);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Webhook_ShouldRespectCurrentReservationDeadline(bool expired)
    {
        var data = new TestData();
        var state = new ProviderState(data);
        try
        {
            await SeedAsync(data);
            using var factory = CreateFactory(data, state,
                now: CreatedAt.AddMinutes(expired ? 10 : 3));
            using var client = CreateClient(factory);
            using var request = CreateRequest(data);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var context = CreateContext(data.TenantId);
            Assert.Equal(
                expired ? AppointmentStatus.Expired : AppointmentStatus.Confirmed,
                (await context.Appointments.SingleAsync()).Status);
            Assert.Equal(PaymentStatus.Confirmed,
                (await context.Payments.SingleAsync()).Status);
            Assert.Equal(PaymentAttemptStatus.Confirmed,
                (await context.PaymentAttempts.SingleAsync()).Status);
            Assert.Equal(
                expired ? AppointmentHistoryType.Expired : AppointmentHistoryType.PaymentConfirmed,
                (await context.AppointmentHistoryEntries.SingleAsync()).Type);
            var receipt = await context.IdempotencyRecords.SingleAsync(
                x => x.Operation == PaymentConfirmationService.Operation);
            var json = JsonNode.Parse(receipt.ResponseJson)!;
            Assert.Equal(expired, json["Result"]!["RequiresCompensation"]!.GetValue<bool>());
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Webhook_WithProviderFailure_ShouldReturnRetryableError(int status)
    {
        var data = new TestData();
        var state = new ProviderState(data) { Status = (HttpStatusCode)status };
        try
        {
            await SeedAsync(data);
            using var factory = CreateFactory(data, state);
            using var client = CreateClient(factory);
            using var request = CreateRequest(data);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Single(state.Paths);
            await AssertPendingAsync(data);
        }
        finally
        {
            await CleanupAsync(data.TenantId);
        }
    }

    #endregion

    #region Helpers

    private WebApplicationFactory<Program> CreateFactory(
        TestData data, ProviderState state,
        bool webhooksEnabled = true, DateTimeOffset? now = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = _fixture.ConnectionString,
            ["Scheduling:Reservations:LifetimeMinutes"] = "10",
            ["Scheduling:Reservations:ExpirationWorkerEnabled"] = "false",
            ["Payments:Enabled"] = "true",
            ["Payments:MercadoPago:Enabled"] = "true",
            ["Payments:MercadoPago:WebhooksEnabled"] = webhooksEnabled ? "true" : "false",
            ["Payments:MercadoPago:Accounts:0:TenantId"] = data.TenantId.ToString(),
            ["Payments:MercadoPago:Accounts:0:BusinessId"] = data.BusinessId.ToString(),
            ["Payments:MercadoPago:Accounts:0:AccessToken"] = "http-test-token",
            ["Payments:MercadoPago:Accounts:0:CollectorId"] = "987654321",
            ["Payments:MercadoPago:Accounts:0:ApplicationId"] = "123456789",
            ["Payments:MercadoPago:Accounts:0:WebhookId"] = data.WebhookId.ToString(),
            ["Payments:MercadoPago:Accounts:0:WebhookSecret"] = Secret
        };
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            foreach (var setting in settings)
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                // Isolate tests from local User Secrets and environment credentials.
                configuration.Sources.Clear();
                configuration.AddInMemoryCollection(settings);
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(_fixture.ConnectionString));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(
                    new FixedTimeProvider(now ?? CreatedAt.AddMinutes(3)));
                services.AddHttpClient<MercadoPagoOrdersClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new ProviderHandler(state));
                services.AddHttpClient<MercadoPagoPaymentsClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new ProviderHandler(state));
            });
        });
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static HttpRequestMessage CreateRequest(
        TestData data, string scenario = "valid", string? orderId = null)
    {
        var signedId = orderId ?? data.OrderId;
        var webhookId = scenario == "unknown-webhook" ? Guid.NewGuid() : data.WebhookId;
        var query = scenario == "missing-data" ? "" :
            $"?data.id={Uri.EscapeDataString(scenario == "changed-data" ? "ORD-OTHER" : signedId)}";
        if (scenario == "duplicate-data") query += $"&data.id={signedId}";
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/webhooks/mercado-pago/{webhookId:D}{query}");
        if (scenario != "missing-request") request.Headers.Add("x-request-id", RequestId);
        if (scenario != "missing-signature")
        {
            var hash = HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(scenario == "wrong-secret" ? "other-secret" : Secret),
                Encoding.UTF8.GetBytes(
                    $"id:{signedId.ToLowerInvariant()};request-id:{RequestId};ts:{Timestamp};"));
            request.Headers.Add("x-signature", scenario == "invalid-signature" ? "invalid" :
                $"ts={Timestamp},v1={Convert.ToHexString(hash)}");
        }
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return request;
    }

    private async Task AssertPendingAsync(TestData data)
    {
        await using var context = CreateContext(data.TenantId);
        Assert.Equal(AppointmentStatus.Pending, (await context.Appointments.SingleAsync()).Status);
        Assert.Equal(PaymentStatus.Pending, (await context.Payments.SingleAsync()).Status);
        Assert.Equal(PaymentAttemptStatus.Pending, (await context.PaymentAttempts.SingleAsync()).Status);
        Assert.Empty(await context.AppointmentHistoryEntries.ToListAsync());
        Assert.Empty(await context.IdempotencyRecords.ToListAsync());
    }

    private async Task SeedAsync(TestData data)
    {
        await using var context = CreateContext(data.TenantId);
        context.Tenants.Add(new Tenant(data.TenantId, "Webhook HTTP Tenant"));
        context.Businesses.Add(new Business(data.BusinessId, data.TenantId,
            BusinessTypeIds.Barbershop, "Webhook HTTP Business", CreatedAt));
        await context.SaveChangesAsync();
        context.Locations.Add(new Location(data.LocationId, data.TenantId,
            data.BusinessId, "Location", "BR", "America/Sao_Paulo", CreatedAt));
        context.Customers.Add(new Customer(data.CustomerId, data.TenantId,
            data.BusinessId, "+5511999990001", CreatedAt, "Customer"));
        context.Professionals.Add(new Professional(data.ProfessionalId,
            data.TenantId, data.BusinessId, "Professional", CreatedAt));
        context.Services.Add(new Service(data.ServiceId, data.TenantId,
            data.BusinessId, "Corte", ServiceType.Single, 65m, 45, CreatedAt));
        await context.SaveChangesAsync();
        context.ProfessionalLocations.Add(new ProfessionalLocation(
            data.TenantId, data.ProfessionalId, data.LocationId, CreatedAt));
        await context.SaveChangesAsync();
        var appointment = new Appointment(data.AppointmentId, data.TenantId,
            data.BusinessId, data.LocationId, data.CustomerId, data.ProfessionalId,
            CreatedAt.AddDays(3), CreatedAt, TimeSpan.FromMinutes(10),
            [new AppointmentItem(Guid.NewGuid(), data.TenantId,
                data.AppointmentId, data.ServiceId, "Corte", 65m, 45)]);
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
        var payment = new Payment(data.PaymentId, appointment, CreatedAt.AddMinutes(1));
        context.Payments.Add(payment);
        await context.SaveChangesAsync();
        var attempt = new PaymentAttempt(data.AttemptId, payment, PaymentMethod.CreditCard,
            CreatedAt.AddMinutes(1), appointment.ReservationExpiresAt!.Value);
        attempt.RegisterProviderPayment("MERCADO_PAGO", data.OrderId, CreatedAt.AddMinutes(1));
        context.PaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();
    }

    private AppDbContext CreateContext(Guid tenantId)
    {
        var tenant = new TenantContext();
        tenant.Initialize(tenantId);
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString).Options, tenant);
    }

    private async Task CleanupAsync(Guid tenantId)
    {
        await using var context = CreateContext(tenantId);
        await context.Payments.Where(x => x.ConfirmedPaymentAttemptId != null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, PaymentStatus.Pending)
                .SetProperty(x => x.ConfirmedPaymentAttemptId, (Guid?)null)
                .SetProperty(x => x.ConfirmedAt, (DateTimeOffset?)null));
        await context.PaymentAttempts.ExecuteDeleteAsync();
        await context.Payments.ExecuteDeleteAsync();
        await context.AppointmentHistoryEntries.ExecuteDeleteAsync();
        await context.AppointmentItems.ExecuteDeleteAsync();
        await context.Appointments.ExecuteDeleteAsync();
        await context.ProfessionalLocations.ExecuteDeleteAsync();
        await context.Customers.ExecuteDeleteAsync();
        await context.Services.ExecuteDeleteAsync();
        await context.Professionals.ExecuteDeleteAsync();
        await context.Locations.ExecuteDeleteAsync();
        await context.IdempotencyRecords.ExecuteDeleteAsync();
        await context.Businesses.ExecuteDeleteAsync();
        await context.Tenants.Where(x => x.Id == tenantId).ExecuteDeleteAsync();
    }

    private sealed class TestData
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid BusinessId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        public Guid CustomerId { get; } = Guid.NewGuid();
        public Guid ProfessionalId { get; } = Guid.NewGuid();
        public Guid ServiceId { get; } = Guid.NewGuid();
        public Guid AppointmentId { get; } = Guid.NewGuid();
        public Guid PaymentId { get; } = Guid.NewGuid();
        public Guid AttemptId { get; } = Guid.NewGuid();
        public Guid WebhookId { get; } = Guid.NewGuid();
        public string OrderId { get; } = $"ORDTST{Guid.NewGuid():N}";
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ProviderState(TestData data)
    {
        public List<string> Paths { get; } = [];
        public List<string?> Tokens { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string OrderJson => new JsonObject
        {
            ["id"] = data.OrderId, ["type"] = "online", ["status"] = "processed",
            ["status_detail"] = "accredited", ["currency"] = "BRL",
            ["user_id"] = "987654321", ["external_reference"] = data.AttemptId.ToString("D"),
            ["total_amount"] = "65.00", ["total_paid_amount"] = "65.00",
            ["integration_data"] = new JsonObject { ["application_id"] = "123456789" },
            ["transactions"] = new JsonObject
            {
                ["payments"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "PAY123", ["reference_id"] = "123456789",
                        ["status"] = "processed", ["status_detail"] = "accredited",
                        ["amount"] = "65", ["paid_amount"] = "65",
                        ["payment_method"] = new JsonObject { ["type"] = "credit_card" }
                    }
                }
            }
        }.ToJsonString();
        public string PaymentJson => new JsonObject
        {
            ["id"] = 123456789L, ["collector_id"] = 987654321L,
            ["status"] = "approved", ["status_detail"] = "accredited",
            ["transaction_amount"] = 65m, ["transaction_amount_refunded"] = 0m,
            ["currency_id"] = "BRL", ["external_reference"] = data.AttemptId.ToString("D"),
            ["date_approved"] = CreatedAt.AddMinutes(2).ToString("O")
        }.ToJsonString();
    }

    private sealed class ProviderHandler(ProviderState state) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("api.mercadopago.com", request.RequestUri.Host);
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri.AbsolutePath;
            state.Paths.Add(path);
            state.Tokens.Add(request.Headers.Authorization?.Parameter);
            var isOrder = path.StartsWith("/v1/orders/", StringComparison.Ordinal);
            if (!isOrder) Assert.Equal("/v1/payments/123456789", path);
            return Task.FromResult(new HttpResponseMessage(state.Status)
            {
                Content = new StringContent(isOrder ? state.OrderJson : state.PaymentJson,
                    Encoding.UTF8, "application/json")
            });
        }
    }

    #endregion
}
