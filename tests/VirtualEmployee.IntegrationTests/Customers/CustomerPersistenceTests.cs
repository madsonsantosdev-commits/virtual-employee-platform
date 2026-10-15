using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Customers;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;
using VirtualEmployee.Infrastructure.Customers;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Customers;

[Collection(PostgreSqlCollection.Name)]
public sealed class CustomerPersistenceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public CustomerPersistenceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Customer_ShouldBeInvisibleToAnotherTenant()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var customerAId = Guid.NewGuid();
        var customerBId = Guid.NewGuid();

        try
        {
            await SeedAsync(tenantAId, businessAId);
            await SeedAsync(tenantBId, businessBId);

            await using (var context = CreateContext(tenantAId))
            {
                context.Customers.Add(
                    new Customer(
                        customerAId,
                        tenantAId,
                        businessAId,
                        "+5511999990001",
                        CreatedAt,
                        "Customer A"));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(tenantBId))
            {
                context.Customers.Add(
                    new Customer(
                        customerBId,
                        tenantBId,
                        businessBId,
                        "+5511999990001",
                        CreatedAt,
                        "Customer B"));

                await context.SaveChangesAsync();
            }

            await using (var contextA = CreateContext(tenantAId))
            {
                var customer = Assert.Single(
                    await contextA.Customers
                        .AsNoTracking()
                        .ToListAsync());

                Assert.Equal(customerAId, customer.Id);
                Assert.Equal(tenantAId, customer.TenantId);

                Assert.False(
                    await contextA.Customers
                        .AnyAsync(x => x.Id == customerBId));

                // Confirma que ambos existem fisicamente.
                var physicalCount = await contextA.Customers
                    .IgnoreQueryFilters()
                    .CountAsync(x =>
                        x.Id == customerAId ||
                        x.Id == customerBId);

                Assert.Equal(2, physicalCount);
            }

            await using var contextB = CreateContext(tenantBId);

            var customerB = Assert.Single(
                await contextB.Customers
                    .AsNoTracking()
                    .ToListAsync());

            Assert.Equal(customerBId, customerB.Id);
            Assert.Equal(tenantBId, customerB.TenantId);

            Assert.False(
                await contextB.Customers
                    .AnyAsync(x => x.Id == customerAId));
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Fact]
    public async Task Customer_WithBusinessFromAnotherTenant_ShouldRejectReference()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        try
        {
            await SeedAsync(tenantAId, businessAId);
            await SeedAsync(tenantBId, businessBId);

            await using (var contextB = CreateContext(tenantBId))
            {
                contextB.Customers.Add(
                    new Customer(
                        customerId,
                        tenantBId,
                        businessAId,
                        "+5511999990001",
                        CreatedAt));

                var exception =
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => contextB.SaveChangesAsync());

                var postgresException =
                    Assert.IsType<Npgsql.PostgresException>(
                        exception.InnerException);

                Assert.Equal(
                    Npgsql.PostgresErrorCodes.ForeignKeyViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    "customers",
                    postgresException.TableName);

                Assert.Equal(
                    "FK_customers_businesses_tenant_id_business_id",
                    postgresException.ConstraintName);
            }

            await using var verificationContext =
                CreateContext(tenantBId);

            Assert.False(
                await verificationContext.Customers
                    .AnyAsync(x => x.Id == customerId));
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

[Fact]
    public async Task CustomerServices_ShouldIsolateCustomersAndBusinessesByTenant()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var customerAId = Guid.NewGuid();
        var customerBId = Guid.NewGuid();

        try
        {
            await SeedAsync(tenantAId, businessAId);
            await SeedAsync(tenantBId, businessBId);

            await using (var contextA = CreateContext(tenantAId))
            {
                var service = new CustomerWriteService(contextA);

                await service.AddAsync(
                    new Customer(
                        customerAId,
                        tenantAId,
                        businessAId,
                        "+5511999990001",
                        CreatedAt,
                        "Customer A"));

                await service.SaveChangesAsync();
            }

            await using (var contextB = CreateContext(tenantBId))
            {
                var service = new CustomerWriteService(contextB);

                await service.AddAsync(
                    new Customer(
                        customerBId,
                        tenantBId,
                        businessBId,
                        "+5511999990001",
                        CreatedAt,
                        "Customer B"));

                await service.SaveChangesAsync();
            }

            await using (var contextA = CreateContext(tenantAId))
            {
                var readService = new CustomerReadService(contextA);
                var writeService = new CustomerWriteService(contextA);

                var customers = await readService.GetAllAsync();

                Assert.Equal(customerAId, Assert.Single(customers).Id);

                Assert.NotNull(
                    await readService.GetByIdAsync(customerAId));

                Assert.Null(
                    await readService.GetByIdAsync(customerBId));

                Assert.Null(
                    await writeService.GetBusinessByIdAsync(businessBId));

                Assert.NotNull(
                    await writeService.GetBusinessByIdAsync(businessAId));
            }

            await using (var contextB = CreateContext(tenantBId))
            {
                var readService = new CustomerReadService(contextB);
                var writeService = new CustomerWriteService(contextB);

                var customers = await readService.GetAllAsync();

                Assert.Equal(customerBId, Assert.Single(customers).Id);

                Assert.NotNull(
                    await readService.GetByIdAsync(customerBId));

                Assert.Null(
                    await readService.GetByIdAsync(customerAId));

                Assert.Null(
                    await writeService.GetBusinessByIdAsync(businessAId));
            }
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Fact]
    public async Task CustomerWriteService_ShouldRejectCustomerFromAnotherTenant()
    {
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        try
        {
            await SeedAsync(ownerTenantId, businessId);

            await using (var otherContext = CreateContext(otherTenantId))
            {
                var service = new CustomerWriteService(otherContext);

                await service.AddAsync(
                    new Customer(
                        customerId,
                        ownerTenantId,
                        businessId,
                        "+5511999990001",
                        CreatedAt));

                var exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => service.SaveChangesAsync());

                Assert.Equal(
                    "Cross-tenant data modification is not allowed.",
                    exception.Message);
            }

            await using var verificationContext =
                CreateContext(ownerTenantId);

            Assert.False(
                await verificationContext.Customers
                    .AnyAsync(customer => customer.Id == customerId));
        }
        finally
        {
            await CleanupAsync(ownerTenantId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Customer_ShouldPersistContactData(
        bool includeOptionalFields)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var name = includeOptionalFields ? "Maria Silva" : null;
        var email = includeOptionalFields ? "maria@example.com" : null;

        try
        {
            await SeedAsync(tenantId, businessId);

            await using (var context = CreateContext(tenantId))
            {
                context.Customers.Add(
                    new Customer(
                        customerId,
                        tenantId,
                        businessId,
                        "+5511999990001",
                        CreatedAt.ToOffset(TimeSpan.FromHours(-3)),
                        name,
                        email));

                await context.SaveChangesAsync();
            }

            await using var readContext = CreateContext(tenantId);

            var customer = await readContext.Customers
                .AsNoTracking()
                .SingleAsync(x => x.Id == customerId);

            Assert.Equal(customerId, customer.Id);
            Assert.Equal(tenantId, customer.TenantId);
            Assert.Equal(businessId, customer.BusinessId);
            Assert.Equal(name, customer.Name);
            Assert.Equal("+5511999990001", customer.Phone);
            Assert.Equal(email, customer.Email);
            Assert.Equal(CreatedAt, customer.CreatedAt);
            Assert.Equal(CreatedAt, customer.UpdatedAt);
            Assert.Equal(TimeSpan.Zero, customer.CreatedAt.Offset);
            Assert.Equal(TimeSpan.Zero, customer.UpdatedAt.Offset);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task Customer_ShouldRejectCrossTenantWrites(
        string operation)
    {
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        try
        {
            await SeedAsync(ownerTenantId, businessId);

            if (operation != "insert")
            {
                await using var context = CreateContext(ownerTenantId);

                context.Customers.Add(
                    new Customer(
                        customerId,
                        ownerTenantId,
                        businessId,
                        "+5511999990001",
                        CreatedAt,
                        "Maria",
                        "maria@example.com"));

                await context.SaveChangesAsync();
            }

            await using (var otherContext = CreateContext(otherTenantId))
            {
                if (operation == "insert")
                {
                    otherContext.Customers.Add(
                        new Customer(
                            customerId,
                            ownerTenantId,
                            businessId,
                            "+5511999990001",
                            CreatedAt,
                            "Maria",
                            "maria@example.com"));
                }
                else
                {
                    var customer = await otherContext.Customers
                        .IgnoreQueryFilters()
                        .SingleAsync(x => x.Id == customerId);

                    if (operation == "update")
                    {
                        customer.Update(
                            "+5511988880002",
                            CreatedAt.AddHours(1),
                            "Changed",
                            "changed@example.com");
                    }
                    else
                    {
                        otherContext.Customers.Remove(customer);
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
                CreateContext(ownerTenantId);

            var persistedCustomer = await verificationContext.Customers
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == customerId);

            if (operation == "insert")
            {
                Assert.Null(persistedCustomer);
            }
            else
            {
                Assert.NotNull(persistedCustomer);
                Assert.Equal(ownerTenantId, persistedCustomer.TenantId);
                Assert.Equal(businessId, persistedCustomer.BusinessId);
                Assert.Equal("Maria", persistedCustomer.Name);
                Assert.Equal("+5511999990001", persistedCustomer.Phone);
                Assert.Equal("maria@example.com", persistedCustomer.Email);
                Assert.Equal(CreatedAt, persistedCustomer.CreatedAt);
                Assert.Equal(CreatedAt, persistedCustomer.UpdatedAt);
            }
        }
        finally
        {
            await CleanupAsync(ownerTenantId);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Customer_WithBlankPhone_ShouldBeRejectedByDatabase(
        string phone)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        try
        {
            await SeedAsync(tenantId, businessId);

            await using (var context = CreateContext(tenantId))
            {
                var customer = new Customer(
                    customerId,
                    tenantId,
                    businessId,
                    "+5511999990001",
                    CreatedAt);

                var entry = context.Customers.Add(customer);

                entry.Property(x => x.Phone).CurrentValue = phone;

                var exception =
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync());

                var postgresException =
                    Assert.IsType<Npgsql.PostgresException>(
                        exception.InnerException);

                Assert.Equal(
                    Npgsql.PostgresErrorCodes.CheckViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    "customers",
                    postgresException.TableName);

                Assert.Equal(
                    "CK_customers_phone_not_blank",
                    postgresException.ConstraintName);
            }

            await using var readContext = CreateContext(tenantId);

            Assert.False(
                await readContext.Customers
                    .AnyAsync(x => x.Id == customerId));
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomerServices_ShouldPersistAndReadContactData(
        bool includeOptionalFields)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var name = includeOptionalFields ? "Maria Silva" : null;
        var email = includeOptionalFields ? "maria@example.com" : null;

        try
        {
            await SeedAsync(tenantId, businessId);

            await using (var writeContext = CreateContext(tenantId))
            {
                var writeService = new CustomerWriteService(writeContext);

                var business = await writeService.GetBusinessByIdAsync(
                    businessId);

                Assert.NotNull(business);
                Assert.Equal(tenantId, business.TenantId);

                await writeService.AddAsync(
                    new Customer(
                        customerId,
                        tenantId,
                        businessId,
                        "+5511999990001",
                        CreatedAt,
                        name,
                        email));

                await writeService.SaveChangesAsync();
            }

            await using var readContext = CreateContext(tenantId);
            var readService = new CustomerReadService(readContext);

            var result = await readService.GetByIdAsync(customerId);

            Assert.NotNull(result);
            Assert.Equal(customerId, result.Id);
            Assert.Equal(businessId, result.BusinessId);
            Assert.Equal(name, result.Name);
            Assert.Equal("+5511999990001", result.Phone);
            Assert.Equal(email, result.Email);

            var customers = await readService.GetAllAsync();

            Assert.Equal(result, Assert.Single(customers));

            Assert.Empty(
                readContext.ChangeTracker.Entries<Customer>());

            Assert.Null(
                await readService.GetByIdAsync(Guid.NewGuid()));
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    private AppDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.Initialize(tenantId);

        return new AppDbContext(options, tenantContext);
    }

    private async Task SeedAsync(Guid tenantId, Guid businessId)
    {
        await using var context = CreateContext(tenantId);

        context.Tenants.Add(
            new Tenant(tenantId, "Customer Tenant"));

        context.Businesses.Add(
            new Business(
                businessId,
                tenantId,
                BusinessTypeIds.Barbershop,
                "Customer Business",
                CreatedAt));

        await context.SaveChangesAsync();
    }

    private async Task CleanupAsync(Guid tenantId)
    {
        await using var context = CreateContext(tenantId);

        context.Customers.RemoveRange(
            await context.Customers.ToListAsync());
        await context.SaveChangesAsync();

        context.Businesses.RemoveRange(
            await context.Businesses.ToListAsync());
        await context.SaveChangesAsync();

        var tenant = await context.Tenants
            .SingleOrDefaultAsync(x => x.Id == tenantId);

        if (tenant is not null)
        {
            context.Tenants.Remove(tenant);
            await context.SaveChangesAsync();
        }
    }
}