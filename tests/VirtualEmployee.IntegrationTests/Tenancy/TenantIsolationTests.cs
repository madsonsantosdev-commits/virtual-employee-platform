using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;

namespace VirtualEmployee.IntegrationTests.Tenancy;

[Collection(PostgreSqlCollection.Name)]
public sealed class TenantIsolationTests
{
    private readonly PostgreSqlFixture _fixture;

    public TenantIsolationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Businesses_ShouldBeIsolatedByTenant()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        var tenantA = new Tenant(
            Guid.NewGuid(),
            "Tenant A");

        var tenantB = new Tenant(
            Guid.NewGuid(),
            "Tenant B");

        var businessA = CreateBusiness(
            Guid.NewGuid(),
            tenantA.Id,
            "Business A");

        var businessB = CreateBusiness(
            Guid.NewGuid(),
            tenantB.Id,
            "Business B");

        var tenantContextA = CreateTenantContext(tenantA.Id);
        var tenantContextB = CreateTenantContext(tenantB.Id);

        // Cria os tenants e o Business A usando Tenant A.
        await using (var dbContext = new AppDbContext(
            options,
            tenantContextA))
        {
            dbContext.Tenants.AddRange(
                tenantA,
                tenantB);

            dbContext.Businesses.Add(businessA);

            await dbContext.SaveChangesAsync();
        }

        // Business B é criado usando Tenant B.
        await using (var dbContext = new AppDbContext(
            options,
            tenantContextB))
        {
            dbContext.Businesses.Add(businessB);

            await dbContext.SaveChangesAsync();
        }

        // Tenant A só pode enxergar Business A.
        await using (var dbContext = new AppDbContext(
            options,
            tenantContextA))
        {
            var businesses = await dbContext.Businesses
                .ToListAsync();

            Assert.Single(businesses);

            Assert.Equal(
                businessA.Id,
                businesses[0].Id);

            Assert.Equal(
                tenantA.Id,
                businesses[0].TenantId);
        }

        // Tenant B só pode enxergar Business B.
        await using (var dbContext = new AppDbContext(
            options,
            tenantContextB))
        {
            var businesses = await dbContext.Businesses
                .ToListAsync();

            Assert.Single(businesses);

            Assert.Equal(
                businessB.Id,
                businesses[0].Id);

            Assert.Equal(
                tenantB.Id,
                businesses[0].TenantId);
        }

        // Remove Business A dentro do Tenant A.
        await using (var dbContext = new AppDbContext(
            options,
            tenantContextA))
        {
            var business = await dbContext.Businesses
                .SingleAsync(x => x.Id == businessA.Id);

            dbContext.Businesses.Remove(business);

            await dbContext.SaveChangesAsync();
        }

        // Remove Business B dentro do Tenant B.
        await using (var dbContext = new AppDbContext(
            options,
            tenantContextB))
        {
            var business = await dbContext.Businesses
                .SingleAsync(x => x.Id == businessB.Id);

            dbContext.Businesses.Remove(business);

            await dbContext.SaveChangesAsync();
        }

        // Tenant é a raiz do isolamento e não é ITenantScoped.
        await using (var dbContext = new AppDbContext(
            options,
            tenantContextA))
        {
            var tenants = await dbContext.Tenants
                .Where(x =>
                    x.Id == tenantA.Id ||
                    x.Id == tenantB.Id)
                .ToListAsync();

            dbContext.Tenants.RemoveRange(tenants);

            await dbContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SaveChanges_WithBusinessFromAnotherTenant_ShouldThrow()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var tenantContextA = CreateTenantContext(tenantAId);

        await using var dbContext = new AppDbContext(
            options,
            tenantContextA);

        var businessB = CreateBusiness(
            Guid.NewGuid(),
            tenantBId,
            "Business B");

        dbContext.Businesses.Add(businessB);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Cross-tenant data modification is not allowed.",
            exception.Message);
    }

    [Fact]
    public async Task SaveChanges_WithModifiedBusinessFromAnotherTenant_ShouldThrow()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var tenantContextA = CreateTenantContext(tenantAId);

        await using var dbContext = new AppDbContext(
            options,
            tenantContextA);

        var businessB = CreateBusiness(
            Guid.NewGuid(),
            tenantBId,
            "Business B");

        dbContext.Attach(businessB);

        dbContext.Entry(businessB).State =
            EntityState.Modified;

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Cross-tenant data modification is not allowed.",
            exception.Message);
    }

    [Fact]
    public async Task SaveChanges_WithDeletedBusinessFromAnotherTenant_ShouldThrow()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var tenantContextA = CreateTenantContext(tenantAId);

        await using var dbContext = new AppDbContext(
            options,
            tenantContextA);

        var businessB = CreateBusiness(
            Guid.NewGuid(),
            tenantBId,
            "Business B");

        dbContext.Attach(businessB);

        dbContext.Entry(businessB).State =
            EntityState.Deleted;

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => dbContext.SaveChangesAsync());

        Assert.Equal(
            "Cross-tenant data modification is not allowed.",
            exception.Message);
    }

    [Fact]
    public async Task BusinessById_FromAnotherTenant_ShouldNotBeFound()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using (var setupContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            setupContext.Tenants.AddRange(
                new Tenant(tenantAId, "Tenant A"),
                new Tenant(tenantBId, "Tenant B"));

            setupContext.Businesses.Add(
                CreateBusiness(
                    businessBId,
                    tenantBId,
                    "Business B"));

            await setupContext.SaveChangesAsync();
        }

        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var business = await tenantAContext.Businesses
                .SingleOrDefaultAsync(x => x.Id == businessBId);

            Assert.Null(business);
        }

        await using (var cleanupContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            var business = await cleanupContext.Businesses
                .SingleAsync(x => x.Id == businessBId);

            cleanupContext.Businesses.Remove(business);

            await cleanupContext.SaveChangesAsync();
        }

        await using (var cleanupTenantContext = new AppDbContext(
            options,
            new TenantContext()))
        {
            var tenants = await cleanupTenantContext.Tenants
                .Where(x =>
                    x.Id == tenantAId ||
                    x.Id == tenantBId)
                .ToListAsync();

            cleanupTenantContext.Tenants.RemoveRange(tenants);

            await cleanupTenantContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Update_WithForgedTenantId_ShouldNotModifyAnotherTenantBusiness()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        // Arrange: Business pertence realmente ao Tenant B.
        await using (var setupContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            setupContext.Tenants.AddRange(
                new Tenant(tenantAId, "Tenant A"),
                new Tenant(tenantBId, "Tenant B"));

            setupContext.Businesses.Add(
                CreateBusiness(
                    businessBId,
                    tenantBId,
                    "Business B"));

            await setupContext.SaveChangesAsync();
        }

        // Attack: Tenant A cria uma entidade desconectada usando
        // o Id do Business B, mas informa TenantId = Tenant A.
        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var forgedBusiness = CreateBusiness(
                businessBId,
                tenantAId,
                "Forged Business");

            tenantAContext.Attach(forgedBusiness);

            tenantAContext.Entry(forgedBusiness).State =
                EntityState.Modified;

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => tenantAContext.SaveChangesAsync());
        }

        // Verifica o estado físico ignorando o Query Filter
        // exclusivamente para validar a barreira de segurança.
        await using (var verificationContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            var business = await verificationContext.Businesses
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == businessBId);

            Assert.Equal(
                "Business B",
                business.Name);

            Assert.Equal(
                tenantBId,
                business.TenantId);
        }

        await using (var cleanupContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            var business = await cleanupContext.Businesses
                .SingleAsync(x => x.Id == businessBId);

            cleanupContext.Businesses.Remove(business);

            await cleanupContext.SaveChangesAsync();
        }

        await using (var cleanupTenantContext = new AppDbContext(
            options,
            new TenantContext()))
        {
            var tenants = await cleanupTenantContext.Tenants
                .Where(x =>
                    x.Id == tenantAId ||
                    x.Id == tenantBId)
                .ToListAsync();

            cleanupTenantContext.Tenants.RemoveRange(tenants);

            await cleanupTenantContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Delete_WithForgedTenantId_ShouldNotDeleteAnotherTenantBusiness()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        // Arrange: Business pertence realmente ao Tenant B.
        await using (var setupContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            setupContext.Tenants.AddRange(
                new Tenant(tenantAId, "Tenant A"),
                new Tenant(tenantBId, "Tenant B"));

            setupContext.Businesses.Add(
                CreateBusiness(
                    businessBId,
                    tenantBId,
                    "Business B"));

            await setupContext.SaveChangesAsync();
        }

        // Tenant A tenta excluir o Business do Tenant B
        // utilizando uma entidade desconectada com TenantId forjado.
        await using (var tenantAContext = new AppDbContext(
            options,
            CreateTenantContext(tenantAId)))
        {
            var forgedBusiness = CreateBusiness(
                businessBId,
                tenantAId,
                "Forged Business");

            tenantAContext.Attach(forgedBusiness);

            tenantAContext.Entry(forgedBusiness).State =
                EntityState.Deleted;

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => tenantAContext.SaveChangesAsync());
        }

        // Verifica o estado físico ignorando o Query Filter
        // exclusivamente para validar a barreira de segurança.
        await using (var verificationContext = new AppDbContext(
            options,
            CreateTenantContext(tenantBId)))
        {
            var business = await verificationContext.Businesses
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.Id == businessBId);

            Assert.NotNull(business);

            Assert.Equal(
                tenantBId,
                business.TenantId);

            Assert.Equal(
                "Business B",
                business.Name);
        }
    }

    private static Business CreateBusiness(
        Guid id,
        Guid tenantId,
        string name)
    {
        return new Business(
            id,
            tenantId,
            BusinessTypeIds.Barbershop,
            name,
            new DateTimeOffset(
                2026, 9, 23,
                12, 0, 0,
                TimeSpan.Zero));
    }

    private static TenantContext CreateTenantContext(Guid tenantId)
    {
        var context = new TenantContext();
        context.Initialize(tenantId);

        return context;
    }
}