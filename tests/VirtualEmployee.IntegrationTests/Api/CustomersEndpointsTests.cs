using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualEmployee.Application.Customers;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class CustomersEndpointsTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public CustomersEndpointsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    #region Facts

    [Fact]
    public async Task Customers_ShouldIsolateListAndLookupByTenant()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        try
        {
            await SeedAsync(tenantAId, businessAId);
            await SeedAsync(tenantBId, businessBId);

            await using var factory = CreateFactory();

            using var clientA = factory.CreateClient();
            clientA.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            using var clientB = factory.CreateClient();
            clientB.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantBId.ToString());

            using var createA = await clientA.PostAsJsonAsync(
                "/api/v1/customers",
                new
                {
                    businessId = businessAId,
                    phone = "+5511999990001",
                    name = "Customer A"
                });

            using var createB = await clientB.PostAsJsonAsync(
                "/api/v1/customers",
                new
                {
                    businessId = businessBId,
                    phone = "+5511999990001",
                    name = "Customer B"
                });

            Assert.Equal(HttpStatusCode.Created, createA.StatusCode);
            Assert.Equal(HttpStatusCode.Created, createB.StatusCode);

            var customerA = await createA.Content
                .ReadFromJsonAsync<CustomerResponse>();

            var customerB = await createB.Content
                .ReadFromJsonAsync<CustomerResponse>();

            Assert.NotNull(customerA);
            Assert.NotNull(customerB);

            using var listA = await clientA.GetAsync("/api/v1/customers");
            using var listB = await clientB.GetAsync("/api/v1/customers");

            Assert.Equal(HttpStatusCode.OK, listA.StatusCode);
            Assert.Equal(HttpStatusCode.OK, listB.StatusCode);

            var customersA = await listA.Content
                .ReadFromJsonAsync<List<CustomerResponse>>();

            var customersB = await listB.Content
                .ReadFromJsonAsync<List<CustomerResponse>>();

            Assert.NotNull(customersA);
            Assert.NotNull(customersB);

            Assert.Equal(customerA, Assert.Single(customersA));
            Assert.Equal(customerB, Assert.Single(customersB));

            using var ownA = await clientA.GetAsync(
                $"/api/v1/customers/{customerA.Id}");

            using var ownB = await clientB.GetAsync(
                $"/api/v1/customers/{customerB.Id}");

            Assert.Equal(HttpStatusCode.OK, ownA.StatusCode);
            Assert.Equal(HttpStatusCode.OK, ownB.StatusCode);

            Assert.Equal(
                customerA,
                await ownA.Content.ReadFromJsonAsync<CustomerResponse>());

            Assert.Equal(
                customerB,
                await ownB.Content.ReadFromJsonAsync<CustomerResponse>());

            using var foreignA = await clientA.GetAsync(
                $"/api/v1/customers/{customerB.Id}");

            using var foreignB = await clientB.GetAsync(
                $"/api/v1/customers/{customerA.Id}");

            Assert.Equal(HttpStatusCode.NotFound, foreignA.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, foreignB.StatusCode);
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Fact]
    public async Task Customers_ShouldCreateAndReturnNormalizedContactData()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        try
        {
            await SeedAsync(tenantId, businessId);

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var createResponse = await client.PostAsJsonAsync(
                "/api/v1/customers",
                new
                {
                    businessId,
                    phone = " +55 (11) 99999-0001 ",
                    name = " Maria Silva ",
                    email = " maria@example.com "
                });

            Assert.Equal(
                HttpStatusCode.Created,
                createResponse.StatusCode);

            var created = await createResponse.Content
                .ReadFromJsonAsync<CustomerResponse>();

            Assert.NotNull(created);
            Assert.NotEqual(Guid.Empty, created.Id);
            Assert.Equal(businessId, created.BusinessId);
            Assert.Equal("Maria Silva", created.Name);
            Assert.Equal("+5511999990001", created.Phone);
            Assert.Equal("maria@example.com", created.Email);

            var expectedPath = $"/api/v1/customers/{created.Id}";

            Assert.NotNull(createResponse.Headers.Location);
            Assert.Equal(
                expectedPath,
                createResponse.Headers.Location.OriginalString);

            using var getResponse = await client.GetAsync(expectedPath);

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var fetched = await getResponse.Content
                .ReadFromJsonAsync<CustomerResponse>();

            Assert.Equal(created, fetched);

            using var listResponse = await client.GetAsync(
                "/api/v1/customers");

            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

            var customers = await listResponse.Content
                .ReadFromJsonAsync<List<CustomerResponse>>();

            Assert.NotNull(customers);
            Assert.Equal(created, Assert.Single(customers));

            await using var context = CreateContext(tenantId);

            var persisted = await context.Customers
                .AsNoTracking()
                .SingleAsync(customer => customer.Id == created.Id);

            Assert.Equal(tenantId, persisted.TenantId);
            Assert.Equal(businessId, persisted.BusinessId);
            Assert.Equal(created.Name, persisted.Name);
            Assert.Equal(created.Phone, persisted.Phone);
            Assert.Equal(created.Email, persisted.Email);
            Assert.Equal(persisted.CreatedAt, persisted.UpdatedAt);
            Assert.Equal(TimeSpan.Zero, persisted.CreatedAt.Offset);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task Customers_WithoutRecords_ShouldReturnEmptyListAndLookupStatuses()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        try
        {
            await SeedAsync(tenantId, businessId);

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var listResponse = await client.GetAsync(
                "/api/v1/customers");

            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

            var customers = await listResponse.Content
                .ReadFromJsonAsync<List<CustomerResponse>>();

            Assert.NotNull(customers);
            Assert.Empty(customers);

            using var missingResponse = await client.GetAsync(
                $"/api/v1/customers/{Guid.NewGuid()}");

            Assert.Equal(
                HttpStatusCode.NotFound,
                missingResponse.StatusCode);

            using var emptyIdResponse = await client.GetAsync(
                $"/api/v1/customers/{Guid.Empty}");

            Assert.Equal(
                HttpStatusCode.BadRequest,
                emptyIdResponse.StatusCode);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData("missing-business")]
    [InlineData("empty-business")]
    [InlineData("missing-phone")]
    [InlineData("null-phone")]
    [InlineData("blank-phone")]
    [InlineData("national-phone")]
    [InlineData("invalid-email")]
    [InlineData("display-name-email")]
    [InlineData("malformed-business")]
    public async Task Customers_WithInvalidPayload_ShouldRejectWithoutPersisting(
        string scenario)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        try
        {
            await SeedAsync(tenantId, businessId);

            var payload = new Dictionary<string, object?>
            {
                ["businessId"] = businessId,
                ["phone"] = "+5511999990001",
                ["name"] = "Maria",
                ["email"] = "maria@example.com"
            };

            switch (scenario)
            {
                case "missing-business":
                    payload.Remove("businessId");
                    break;

                case "empty-business":
                    payload["businessId"] = Guid.Empty;
                    break;

                case "missing-phone":
                    payload.Remove("phone");
                    break;

                case "null-phone":
                    payload["phone"] = null;
                    break;

                case "blank-phone":
                    payload["phone"] = "   ";
                    break;

                case "national-phone":
                    payload["phone"] = "(11) 99999-0001";
                    break;

                case "invalid-email":
                    payload["email"] = "invalid-email";
                    break;

                case "display-name-email":
                    payload["email"] = "Maria <maria@example.com>";
                    break;

                case "malformed-business":
                    payload["businessId"] = "invalid-guid";
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/customers",
                payload);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            await using var context = CreateContext(tenantId);

            Assert.False(await context.Customers.AnyAsync());
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("other-tenant")]
    [InlineData("inactive")]
    public async Task Customers_WithUnavailableBusiness_ShouldRejectWithoutPersisting(
        string scenario)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var otherBusinessId = Guid.NewGuid();

        try
        {
            await SeedAsync(tenantId, businessId);
            await SeedAsync(otherTenantId, otherBusinessId);

            var requestedBusinessId = scenario switch
            {
                "missing" => Guid.NewGuid(),
                "other-tenant" => otherBusinessId,
                "inactive" => businessId,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };

            if (scenario == "inactive")
            {
                await using var context = CreateContext(tenantId);

                var business = await context.Businesses
                    .SingleAsync(entity => entity.Id == businessId);

                business.Update(
                    business.BusinessTypeId,
                    business.Name,
                    business.SlotIntervalMinutes,
                    false,
                    CreatedAt.AddHours(1));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/customers",
                new
                {
                    businessId = requestedBusinessId,
                    phone = "+5511999990001",
                    name = "Maria"
                });

            var expectedStatus = scenario == "inactive"
                ? HttpStatusCode.Conflict
                : HttpStatusCode.NotFound;

            Assert.Equal(expectedStatus, response.StatusCode);

            await using var verificationContext = CreateContext(tenantId);

            Assert.False(
                await verificationContext.Customers
                    .IgnoreQueryFilters()
                    .AnyAsync(customer =>
                        customer.TenantId == tenantId ||
                        customer.TenantId == otherTenantId));
        }
        finally
        {
            await CleanupAsync(tenantId);
            await CleanupAsync(otherTenantId);
        }
    }

    [Theory]
    [InlineData("omitted")]
    [InlineData("null")]
    [InlineData("blank")]
    public async Task Customers_WithOptionalFieldsEmpty_ShouldCreate(
        string scenario)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        try
        {
            await SeedAsync(tenantId, businessId);

            var payload = new Dictionary<string, object?>
            {
                ["businessId"] = businessId,
                ["phone"] = "+5511999990001"
            };

            if (scenario != "omitted")
            {
                payload["name"] = scenario == "null" ? null : "   ";
                payload["email"] = scenario == "null" ? null : "   ";
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/customers",
                payload);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var customer = await response.Content
                .ReadFromJsonAsync<CustomerResponse>();

            Assert.NotNull(customer);
            Assert.Null(customer.Name);
            Assert.Null(customer.Email);
            Assert.Equal("+5511999990001", customer.Phone);

            await using var context = CreateContext(tenantId);

            var persisted = await context.Customers
                .AsNoTracking()
                .SingleAsync(entity => entity.Id == customer.Id);

            Assert.Null(persisted.Name);
            Assert.Null(persisted.Email);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    #endregion

    #region Helpers

    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                builder.UseSetting(
                    "ConnectionStrings:Database",
                    _fixture.ConnectionString);

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<AppDbContext>>();
                    services.RemoveAll<AppDbContext>();

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseNpgsql(_fixture.ConnectionString));
                });
            });
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
            new Tenant(tenantId, "Customer HTTP Tenant"));

        context.Businesses.Add(
            new Business(
                businessId,
                tenantId,
                BusinessTypeIds.Barbershop,
                "Customer HTTP Business",
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
            .SingleOrDefaultAsync(entity => entity.Id == tenantId);

        if (tenant is not null)
        {
            context.Tenants.Remove(tenant);
            await context.SaveChangesAsync();
        }
    }

    #endregion
}