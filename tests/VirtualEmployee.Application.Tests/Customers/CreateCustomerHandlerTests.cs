using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Application.Customers;
using VirtualEmployee.Application.Customers.CreateCustomer;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Customers;
using Xunit;

namespace VirtualEmployee.Application.Tests.Customers;

public sealed class CreateCustomerHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleAsync_ShouldPersistNormalizedCustomer(
        bool includeOptionalFields)
    {
        var tenantId = Guid.NewGuid();
        var service = new FakeWriteService(tenantId);

        var handler = CreateHandler(service, tenantId);

        var result = await handler.HandleAsync(
            new CreateCustomerCommand(
                service.Business!.Id,
                "  +55 (11) 99999-0001  ",
                includeOptionalFields ? "  Maria Silva  " : "   ",
                includeOptionalFields ? "  Maria@example.com  " : "   "));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response);
        Assert.NotNull(service.AddedCustomer);
        Assert.True(service.Saved);

        var customer = service.AddedCustomer;

        Assert.NotEqual(Guid.Empty, customer.Id);
        Assert.Equal(tenantId, customer.TenantId);
        Assert.Equal(service.Business.Id, customer.BusinessId);
        Assert.Equal("+5511999990001", customer.Phone);
        Assert.Equal(
            includeOptionalFields ? "Maria Silva" : null,
            customer.Name);
        Assert.Equal(
            includeOptionalFields ? "Maria@example.com" : null,
            customer.Email);

        // O relógio possui fração de 100 ns; a gravação usa microssegundos.
        Assert.Equal(Now, customer.CreatedAt);
        Assert.Equal(Now, customer.UpdatedAt);

        Assert.Equal(customer.Id, result.Response.Id);
        Assert.Equal(customer.BusinessId, result.Response.BusinessId);
        Assert.Equal(customer.Name, result.Response.Name);
        Assert.Equal(customer.Phone, result.Response.Phone);
        Assert.Equal(customer.Email, result.Response.Email);
    }

    [Theory]
    [InlineData("empty-business")]
    [InlineData("null-phone")]
    [InlineData("blank-phone")]
    [InlineData("national-phone")]
    [InlineData("invalid-email")]
    [InlineData("display-name-email")]
    [InlineData("multiple-emails")]
    [InlineData("control-in-email")]
    public async Task HandleAsync_WithInvalidRequest_ShouldNotReadOrWrite(
        string scenario)
    {
        var tenantId = Guid.NewGuid();
        var service = new FakeWriteService(tenantId);
        var command = new CreateCustomerCommand(
            service.Business!.Id,
            "+5511999990001");

        command = scenario switch
        {
            "empty-business" => command with
            {
                BusinessId = Guid.Empty
            },
            "null-phone" => command with
            {
                Phone = null!
            },
            "blank-phone" => command with
            {
                Phone = "   "
            },
            "national-phone" => command with
            {
                Phone = "11999990001"
            },
            "invalid-email" => command with
            {
                Email = "invalid-email"
            },
            "display-name-email" => command with
            {
                Email = "Maria <maria@example.com>"
            },
            "multiple-emails" => command with
            {
                Email = "maria@example.com,other@example.com"
            },
            "control-in-email" => command with
            {
                Email = "maria\u0001@example.com"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var result = await CreateHandler(service, tenantId)
            .HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(CreateCustomerError.InvalidRequest, result.Error);
        Assert.Null(result.Response);
        Assert.Equal(0, service.BusinessReadCount);
        Assert.Null(service.AddedCustomer);
        Assert.False(service.Saved);
    }

    [Theory]
    [InlineData("missing", CreateCustomerError.BusinessNotFound)]
    [InlineData("other-tenant", CreateCustomerError.BusinessNotFound)]
    [InlineData("inactive", CreateCustomerError.InactiveBusiness)]
    public async Task HandleAsync_WithUnavailableBusiness_ShouldNotWrite(
        string scenario,
        CreateCustomerError expectedError)
    {
        var tenantId = Guid.NewGuid();
        var service = new FakeWriteService(
            scenario == "other-tenant" ? Guid.NewGuid() : tenantId);

        var businessId = service.Business!.Id;

        if (scenario == "missing")
        {
            service.Business = null;
        }
        else if (scenario == "inactive")
        {
            service.Business.Update(
                service.Business.BusinessTypeId,
                service.Business.Name,
                service.Business.SlotIntervalMinutes,
                false,
                Now);
        }

        var result = await CreateHandler(service, tenantId)
            .HandleAsync(
                new CreateCustomerCommand(
                    businessId,
                    "+5511999990001"));

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        Assert.Null(result.Response);
        Assert.Null(service.AddedCustomer);
        Assert.False(service.Saved);
    }

    private static CreateCustomerHandler CreateHandler(
        FakeWriteService service,
        Guid tenantId)
    {
        return new CreateCustomerHandler(
            service,
            new FakeTenantContext(tenantId),
            new FixedTimeProvider(Now.AddTicks(7)));
    }

    private sealed class FakeTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool IsInitialized => tenantId != Guid.Empty;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeWriteService : ICustomerWriteService
    {
        public Business? Business { get; set; }
        public Customer? AddedCustomer { get; private set; }
        public bool Saved { get; private set; }
        public int BusinessReadCount { get; private set; }

        public FakeWriteService(Guid tenantId)
        {
            Business = new Business(
                Guid.NewGuid(),
                tenantId,
                BusinessTypeIds.Barbershop,
                "Customer Business",
                Now);
        }

        public Task<Business?> GetBusinessByIdAsync(
            Guid businessId,
            CancellationToken cancellationToken = default)
        {
            BusinessReadCount++;

            return Task.FromResult(
                Business is not null && Business.Id == businessId
                    ? Business
                    : null);
        }

        public Task AddAsync(
            Customer customer,
            CancellationToken cancellationToken = default)
        {
            AddedCustomer = customer;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            Saved = true;
            return Task.CompletedTask;
        }
    }
}