using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualEmployee.Application.Professionals;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;

namespace VirtualEmployee.IntegrationTests.Api;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProfessionalsEndpointsTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public ProfessionalsEndpointsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetProfessionals_ShouldReturnOnlyProfessionalsFromCurrentTenant()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalAId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantAId,
                businessAId,
                professionalAId,
                "Professional A");

            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                "Professional B");

            // Confirms that both records physically exist.
            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId)))
            {
                var professionals = await context.Professionals
                    .IgnoreQueryFilters()
                    .Where(x =>
                        x.Id == professionalAId ||
                        x.Id == professionalBId)
                    .ToListAsync();

                Assert.Equal(2, professionals.Count);

                Assert.Contains(
                    professionals,
                    x => x.Id == professionalAId &&
                         x.TenantId == tenantAId);

                Assert.Contains(
                    professionals,
                    x => x.Id == professionalBId &&
                         x.TenantId == tenantBId);
            }

            await using var factory = CreateFactory();

            using var clientA = factory.CreateClient();
            clientA.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            using var responseA = await clientA.GetAsync(
                "/api/v1/professionals");

            Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);

            var professionalsA = await responseA.Content
                .ReadFromJsonAsync<List<ProfessionalResponse>>();

            Assert.NotNull(professionalsA);

            var professionalA = Assert.Single(professionalsA);

            Assert.Equal(professionalAId, professionalA.Id);
            Assert.Equal(businessAId, professionalA.BusinessId);
            Assert.Equal("Professional A", professionalA.Name);
            Assert.True(professionalA.IsActive);

            using var clientB = factory.CreateClient();
            clientB.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantBId.ToString());

            using var responseB = await clientB.GetAsync(
                "/api/v1/professionals");

            Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);

            var professionalsB = await responseB.Content
                .ReadFromJsonAsync<List<ProfessionalResponse>>();

            Assert.NotNull(professionalsB);

            var professionalB = Assert.Single(professionalsB);

            Assert.Equal(professionalBId, professionalB.Id);
            Assert.Equal(businessBId, professionalB.BusinessId);
            Assert.Equal("Professional B", professionalB.Name);
            Assert.True(professionalB.IsActive);
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    private async Task SeedAsync(
        Guid tenantId,
        Guid businessId,
        Guid professionalId,
        string name)
    {
        await using var context = new AppDbContext(
            CreateOptions(),
            CreateTenantContext(tenantId));

        context.Tenants.Add(
            new Tenant(tenantId, $"HTTP Tenant {tenantId}"));

        context.Businesses.Add(
            new Business(
                businessId,
                tenantId,
                BusinessTypeIds.Barbershop,
                "HTTP Business",
                CreatedAt));

        await context.SaveChangesAsync();

        context.Professionals.Add(
            new Professional(
                professionalId,
                tenantId,
                businessId,
                name,
                CreatedAt));

        await context.SaveChangesAsync();
    }

    private async Task CleanupAsync(Guid tenantId)
    {
        await using var context = new AppDbContext(
            CreateOptions(),
            CreateTenantContext(tenantId));

        // These tenants are generated exclusively for each test.
        var professionals = await context.Professionals
            .Where(x => x.TenantId == tenantId)
            .ToListAsync();

        context.Professionals.RemoveRange(professionals);
        await context.SaveChangesAsync();

        var businesses = await context.Businesses
            .Where(x => x.TenantId == tenantId)
            .ToListAsync();

        context.Businesses.RemoveRange(businesses);
        await context.SaveChangesAsync();

        var tenant = await context.Tenants
            .SingleOrDefaultAsync(x => x.Id == tenantId);

        if (tenant is not null)
        {
            context.Tenants.Remove(tenant);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task CreateProfessional_WithBusinessFromCurrentTenant_ShouldReturnCreated()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var options = CreateOptions();

        try
        {
            await using (var seedContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId)))
            {
                seedContext.Tenants.Add(
                    new Tenant(tenantId, "Create Professional Tenant"));

                seedContext.Businesses.Add(
                    new Business(
                        businessId,
                        tenantId,
                        BusinessTypeIds.Barbershop,
                        "Create Professional Business",
                        CreatedAt));

                await seedContext.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/professionals",
                new
                {
                    businessId,
                    name = "  Arthur Silva  "
                });

            Assert.Equal(
                HttpStatusCode.Created,
                response.StatusCode);

            var professional = await response.Content
                .ReadFromJsonAsync<ProfessionalResponse>();

            Assert.NotNull(professional);
            Assert.NotEqual(Guid.Empty, professional.Id);
            Assert.Equal(businessId, professional.BusinessId);
            Assert.Equal("Arthur Silva", professional.Name);
            Assert.True(professional.IsActive);

            Assert.Equal(
                $"/api/v1/professionals/{professional.Id}",
                response.Headers.Location?.ToString());

            await using var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId));

            var persistedProfessional = await verificationContext
                .Professionals
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == professional.Id);

            Assert.Equal(tenantId, persistedProfessional.TenantId);
            Assert.Equal(businessId, persistedProfessional.BusinessId);
            Assert.Equal("Arthur Silva", persistedProfessional.Name);
            Assert.True(persistedProfessional.IsActive);

            Assert.Equal(
                persistedProfessional.CreatedAt,
                persistedProfessional.UpdatedAt);

            Assert.True(
                persistedProfessional.CreatedAt > CreatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task CreateProfessional_WithBusinessFromAnotherTenant_ShouldReturnNotFound()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();
        var options = CreateOptions();

        try
        {
            await using (var context = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                context.Tenants.Add(
                    new Tenant(tenantAId, "Create Professional Tenant A"));

                await context.SaveChangesAsync();
            }

            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                "Professional B");

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/professionals",
                new
                {
                    businessId = businessBId,
                    name = "Cross Tenant Professional"
                });

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            await using var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantAId));

            // Confirms that the requested Business physically exists.
            var business = await verificationContext.Businesses
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == businessBId);

            Assert.Equal(tenantBId, business.TenantId);

            // Checks both tenants, including any incorrectly inserted row.
            var professionals = await verificationContext.Professionals
                .IgnoreQueryFilters()
                .Where(x =>
                    x.TenantId == tenantAId ||
                    x.TenantId == tenantBId ||
                    x.BusinessId == businessBId)
                .ToListAsync();

            var professional = Assert.Single(professionals);

            Assert.Equal(professionalBId, professional.Id);
            Assert.Equal(tenantBId, professional.TenantId);
            Assert.Equal(businessBId, professional.BusinessId);
            Assert.Equal("Professional B", professional.Name);
            Assert.True(professional.IsActive);
            Assert.Equal(CreatedAt, professional.CreatedAt);
            Assert.Equal(CreatedAt, professional.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Fact]
    public async Task UpdateProfessional_FromCurrentTenant_ShouldReturnOkAndPersistChanges()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional Original");

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalId}",
                new
                {
                    name = "  Professional Atualizado  ",
                    isActive = false
                });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var professional = await response.Content
                .ReadFromJsonAsync<ProfessionalResponse>();

            Assert.NotNull(professional);
            Assert.Equal(professionalId, professional.Id);
            Assert.Equal(businessId, professional.BusinessId);
            Assert.Equal("Professional Atualizado", professional.Name);
            Assert.False(professional.IsActive);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var persistedProfessional = await verificationContext
                .Professionals
                .SingleAsync(x => x.Id == professionalId);

            Assert.Equal(professionalId, persistedProfessional.Id);
            Assert.Equal(tenantId, persistedProfessional.TenantId);
            Assert.Equal(businessId, persistedProfessional.BusinessId);
            Assert.Equal("Professional Atualizado", persistedProfessional.Name);
            Assert.False(persistedProfessional.IsActive);
            Assert.Equal(CreatedAt, persistedProfessional.CreatedAt);
            Assert.True(persistedProfessional.UpdatedAt > CreatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task UpdateProfessional_FromAnotherTenant_ShouldReturnNotFoundAndPreserveProfessional()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();
        var options = CreateOptions();

        try
        {
            await using (var context = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                context.Tenants.Add(
                    new Tenant(tenantAId, "Update Professional Tenant A"));

                await context.SaveChangesAsync();
            }

            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                "Professional Original");

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            using var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalBId}",
                new
                {
                    name = "Alteração indevida",
                    isActive = false
                });

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            await using var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantAId));

            var professional = await verificationContext.Professionals
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == professionalBId);

            Assert.Equal(professionalBId, professional.Id);
            Assert.Equal(tenantBId, professional.TenantId);
            Assert.Equal(businessBId, professional.BusinessId);
            Assert.Equal("Professional Original", professional.Name);
            Assert.True(professional.IsActive);
            Assert.Equal(CreatedAt, professional.CreatedAt);
            Assert.Equal(CreatedAt, professional.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Fact]
    public async Task GetProfessionalById_ShouldReturnOnlyToCurrentTenant()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var professionalAId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantAId,
                businessAId,
                professionalAId,
                "Professional A");

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantBId)))
            {
                context.Tenants.Add(
                    new Tenant(tenantBId, "Get Professional Tenant B"));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();

            using var ownerClient = factory.CreateClient();
            ownerClient.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantAId.ToString());

            using var ownerResponse = await ownerClient.GetAsync(
                $"/api/v1/professionals/{professionalAId}");

            Assert.Equal(
                HttpStatusCode.OK,
                ownerResponse.StatusCode);

            var professional = await ownerResponse.Content
                .ReadFromJsonAsync<ProfessionalResponse>();

            Assert.NotNull(professional);
            Assert.Equal(professionalAId, professional.Id);
            Assert.Equal(businessAId, professional.BusinessId);
            Assert.Equal("Professional A", professional.Name);
            Assert.True(professional.IsActive);

            using var otherTenantClient = factory.CreateClient();
            otherTenantClient.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantBId.ToString());

            using var otherTenantResponse = await otherTenantClient.GetAsync(
                $"/api/v1/professionals/{professionalAId}");

            Assert.Equal(
                HttpStatusCode.NotFound,
                otherTenantResponse.StatusCode);
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Fact]
    public async Task CreateProfessional_WithUnknownBusiness_ShouldReturnNotFound()
    {
        var tenantId = Guid.NewGuid();
        var unknownBusinessId = Guid.NewGuid();

        try
        {
            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Tenants.Add(
                    new Tenant(tenantId, "Unknown Business Tenant"));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/professionals",
                new
                {
                    businessId = unknownBusinessId,
                    name = "Professional Sem Business"
                });

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            Assert.False(
                await verificationContext.Businesses
                    .IgnoreQueryFilters()
                    .AnyAsync(x => x.Id == unknownBusinessId));

            Assert.False(
                await verificationContext.Professionals
                    .IgnoreQueryFilters()
                    .AnyAsync(x =>
                        x.TenantId == tenantId ||
                        x.BusinessId == unknownBusinessId));
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task Professional_WithUnknownId_ShouldReturnNotFound(
        string method)
    {
        var tenantId = Guid.NewGuid();

        try
        {
            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Tenants.Add(
                    new Tenant(tenantId, "Unknown Professional Tenant"));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            var url = $"/api/v1/professionals/{Guid.NewGuid()}";

            using var response = method == "GET"
                ? await client.GetAsync(url)
                : await client.PutAsJsonAsync(
                    url,
                    new
                    {
                        name = "Professional Inexistente",
                        isActive = false
                    });

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            Assert.False(
                await verificationContext.Professionals.AnyAsync());
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(null, "TENANT_HEADER_REQUIRED")]
    [InlineData("tenant-invalido", "INVALID_TENANT_ID")]
    public async Task GetProfessionals_WithMissingOrInvalidTenantHeader_ShouldReturnBadRequest(
        string? tenantHeader,
        string expectedCode)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        if (tenantHeader is not null)
        {
            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantHeader);
        }

        using var response = await client.GetAsync(
            "/api/v1/professionals");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains(expectedCode, body);
    }

    [Theory]
    [InlineData(true, 10)]
    [InlineData(false, 0)]
    [InlineData(false, 3)]
    [InlineData(false, 161)]
    public async Task CreateProfessional_WithInvalidRequest_ShouldReturnBadRequestAndNotPersist(
        bool emptyBusinessId,
        int nameLength)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var name = nameLength <= 3
            ? new string(' ', nameLength)
            : new string('A', nameLength);

        try
        {
            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Tenants.Add(
                    new Tenant(tenantId, "Invalid Create Tenant"));

                context.Businesses.Add(
                    new Business(
                        businessId,
                        tenantId,
                        BusinessTypeIds.Barbershop,
                        "Invalid Create Business",
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/professionals",
                new
                {
                    businessId = emptyBusinessId
                        ? Guid.Empty
                        : businessId,
                    name
                });

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            Assert.False(
                await verificationContext.Professionals
                    .IgnoreQueryFilters()
                    .AnyAsync(x =>
                        x.TenantId == tenantId ||
                        x.BusinessId == businessId));
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(161)]
    public async Task UpdateProfessional_WithInvalidName_ShouldReturnBadRequestAndPreserveProfessional(
        int nameLength)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();

        var invalidName = nameLength == 161
            ? new string('A', nameLength)
            : new string(' ', nameLength);

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                "Professional Original");

            await using var factory = CreateFactory();
            using var client = factory.CreateClient();

            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id",
                tenantId.ToString());

            using var response = await client.PutAsJsonAsync(
                $"/api/v1/professionals/{professionalId}",
                new
                {
                    name = invalidName,
                    isActive = false
                });

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var professional = await verificationContext.Professionals
                .SingleAsync(x => x.Id == professionalId);

            Assert.Equal(professionalId, professional.Id);
            Assert.Equal(tenantId, professional.TenantId);
            Assert.Equal(businessId, professional.BusinessId);
            Assert.Equal("Professional Original", professional.Name);
            Assert.True(professional.IsActive);
            Assert.Equal(CreatedAt, professional.CreatedAt);
            Assert.Equal(CreatedAt, professional.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }
    private DbContextOptions<AppDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
    }

    private static TenantContext CreateTenantContext(Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.Initialize(tenantId);

        return tenantContext;
    }

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
}