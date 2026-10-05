using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;
using VirtualEmployee.Infrastructure.Professionals;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Professionals;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProfessionalPersistenceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public ProfessionalPersistenceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Professional_ShouldPersistAndLoadOperationalData()
    {
        var options = CreateOptions();

        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();

        try
        {
            await using (var writeContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId)))
            {
                writeContext.Tenants.Add(
                    new Tenant(tenantId, "Tenant Professionals"));

                writeContext.Businesses.Add(
                    new Business(
                        businessId,
                        tenantId,
                        BusinessTypeIds.Barbershop,
                        "Business Professionals",
                        CreatedAt));

                writeContext.Professionals.Add(
                    new Professional(
                        professionalId,
                        tenantId,
                        businessId,
                        "  Arthur Silva  ",
                        CreatedAt));

                await writeContext.SaveChangesAsync();
            }

            await using (var readContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId)))
            {
                var professional = await readContext.Professionals
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == professionalId);

                Assert.Equal(professionalId, professional.Id);
                Assert.Equal(tenantId, professional.TenantId);
                Assert.Equal(businessId, professional.BusinessId);
                Assert.Equal("Arthur Silva", professional.Name);
                Assert.True(professional.IsActive);
                Assert.Equal(CreatedAt, professional.CreatedAt);
                Assert.Equal(CreatedAt, professional.UpdatedAt);
            }
        }
        finally
        {
            await RemoveTestDataAsync(
                options,
                tenantId,
                businessId,
                professionalId);
        }
    }

    [Fact]
    public async Task Professional_Update_ShouldPersistChangesAndPreserveIdentity()
    {
        var options = CreateOptions();

        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var updatedAt = CreatedAt.AddHours(1);

        try
        {
            await using (var setupContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId)))
            {
                setupContext.Tenants.Add(
                    new Tenant(tenantId, "Tenant Professionals"));

                setupContext.Businesses.Add(
                    new Business(
                        businessId,
                        tenantId,
                        BusinessTypeIds.Barbershop,
                        "Business Professionals",
                        CreatedAt));

                setupContext.Professionals.Add(
                    new Professional(
                        professionalId,
                        tenantId,
                        businessId,
                        "Arthur Silva",
                        CreatedAt));

                await setupContext.SaveChangesAsync();
            }

            await using (var updateContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId)))
            {
                var professional = await updateContext.Professionals
                    .SingleAsync(x => x.Id == professionalId);

                professional.Update(
                    "  Arthur Santos  ",
                    false,
                    updatedAt);

                await updateContext.SaveChangesAsync();
            }

            await using (var readContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId)))
            {
                var professional = await readContext.Professionals
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == professionalId);

                Assert.Equal(professionalId, professional.Id);
                Assert.Equal(tenantId, professional.TenantId);
                Assert.Equal(businessId, professional.BusinessId);
                Assert.Equal("Arthur Santos", professional.Name);
                Assert.False(professional.IsActive);
                Assert.Equal(CreatedAt, professional.CreatedAt);
                Assert.Equal(updatedAt, professional.UpdatedAt);
            }
        }
        finally
        {
            await RemoveTestDataAsync(
                options,
                tenantId,
                businessId,
                professionalId);
        }
    }

    [Fact]
    public async Task Professionals_ShouldBeIsolatedByTenant()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var professionalAId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();

        try
        {
            await using (var contextA = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                contextA.Tenants.Add(
                    new Tenant(tenantAId, "Tenant A"));

                contextA.Businesses.Add(
                    new Business(
                        businessAId,
                        tenantAId,
                        BusinessTypeIds.Barbershop,
                        "Business A",
                        CreatedAt));

                contextA.Professionals.Add(
                    new Professional(
                        professionalAId,
                        tenantAId,
                        businessAId,
                        "Professional A",
                        CreatedAt));

                await contextA.SaveChangesAsync();
            }

            await using (var contextB = new AppDbContext(
                options,
                CreateTenantContext(tenantBId)))
            {
                contextB.Tenants.Add(
                    new Tenant(tenantBId, "Tenant B"));

                contextB.Businesses.Add(
                    new Business(
                        businessBId,
                        tenantBId,
                        BusinessTypeIds.Barbershop,
                        "Business B",
                        CreatedAt));

                contextB.Professionals.Add(
                    new Professional(
                        professionalBId,
                        tenantBId,
                        businessBId,
                        "Professional B",
                        CreatedAt));

                await contextB.SaveChangesAsync();
            }

            await using (var readContextA = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                var professionals = await readContextA.Professionals
                    .AsNoTracking()
                    .ToListAsync();

                var professional = Assert.Single(professionals);

                Assert.Equal(professionalAId, professional.Id);
                Assert.Equal(tenantAId, professional.TenantId);

                var professionalFromTenantB =
                    await readContextA.Professionals
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            x => x.Id == professionalBId);

                Assert.Null(professionalFromTenantB);
            }

            await using (var readContextB = new AppDbContext(
                options,
                CreateTenantContext(tenantBId)))
            {
                var professionals = await readContextB.Professionals
                    .AsNoTracking()
                    .ToListAsync();

                var professional = Assert.Single(professionals);

                Assert.Equal(professionalBId, professional.Id);
                Assert.Equal(tenantBId, professional.TenantId);

                var professionalFromTenantA =
                    await readContextB.Professionals
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            x => x.Id == professionalAId);

                Assert.Null(professionalFromTenantA);
            }
        }
        finally
        {
            try
            {
                await RemoveTestDataAsync(
                    options,
                    tenantAId,
                    businessAId,
                    professionalAId);
            }
            finally
            {
                await RemoveTestDataAsync(
                    options,
                    tenantBId,
                    businessBId,
                    professionalBId);
            }
        }
    }

    [Fact]
    public async Task Professional_WithBusinessFromAnotherTenant_ShouldBeRejected()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();

        try
        {
            await using (var setupContext = new AppDbContext(
                options,
                CreateTenantContext(tenantBId)))
            {
                setupContext.Tenants.AddRange(
                    new Tenant(tenantAId, "Tenant A"),
                    new Tenant(tenantBId, "Tenant B"));

                setupContext.Businesses.Add(
                    new Business(
                        businessBId,
                        tenantBId,
                        BusinessTypeIds.Barbershop,
                        "Business B",
                        CreatedAt));

                await setupContext.SaveChangesAsync();
            }

            await using (var writeContext = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                writeContext.Professionals.Add(
                    new Professional(
                        professionalId,
                        tenantAId,
                        businessBId,
                        "Invalid Professional",
                        CreatedAt));

                var exception = await Assert.ThrowsAsync<DbUpdateException>(
                    () => writeContext.SaveChangesAsync());

                var postgresException =
                    Assert.IsType<Npgsql.PostgresException>(
                        exception.InnerException);

                Assert.Equal(
                    Npgsql.PostgresErrorCodes.ForeignKeyViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    "FK_professionals_businesses_tenant_id_business_id",
                    postgresException.ConstraintName);
            }

            await using (var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                var exists = await verificationContext.Professionals
                    .IgnoreQueryFilters()
                    .AnyAsync(x => x.Id == professionalId);

                Assert.False(exists);
            }
        }
        finally
        {
            try
            {
                await RemoveTestDataAsync(
                    options,
                    tenantAId,
                    businessBId,
                    professionalId);
            }
            finally
            {
                await RemoveTestDataAsync(
                    options,
                    tenantBId,
                    businessBId,
                    professionalId);
            }
        }
    }

    [Fact]
    public async Task ProfessionalWriteService_ShouldCreateAndUpdateProfessional()
    {
        var options = CreateOptions();

        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var updatedAt = CreatedAt.AddHours(1);

        try
        {
            await using (var setupContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId)))
            {
                setupContext.Tenants.Add(
                    new Tenant(tenantId, "Tenant Professionals"));

                setupContext.Businesses.Add(
                    new Business(
                        businessId,
                        tenantId,
                        BusinessTypeIds.Barbershop,
                        "Business Professionals",
                        CreatedAt));

                await setupContext.SaveChangesAsync();
            }

            await using (var createContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId)))
            {
                var writeService =
                    new ProfessionalWriteService(createContext);

                Assert.True(
                    await writeService.BusinessExistsAsync(businessId));

                await writeService.AddAsync(
                    new Professional(
                        professionalId,
                        tenantId,
                        businessId,
                        "Arthur Silva",
                        CreatedAt));
            }

            await using (var updateContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId)))
            {
                var writeService =
                    new ProfessionalWriteService(updateContext);

                var professional =
                    await writeService.GetByIdAsync(professionalId);

                Assert.NotNull(professional);
                Assert.Equal("Arthur Silva", professional.Name);
                Assert.True(professional.IsActive);

                professional.Update(
                    "Arthur Santos",
                    false,
                    updatedAt);

                await writeService.SaveChangesAsync();
            }

            await using (var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantId)))
            {
                var professional = await verificationContext.Professionals
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == professionalId);

                Assert.Equal(professionalId, professional.Id);
                Assert.Equal(tenantId, professional.TenantId);
                Assert.Equal(businessId, professional.BusinessId);
                Assert.Equal("Arthur Santos", professional.Name);
                Assert.False(professional.IsActive);
                Assert.Equal(CreatedAt, professional.CreatedAt);
                Assert.Equal(updatedAt, professional.UpdatedAt);
            }
        }
        finally
        {
            await RemoveTestDataAsync(
                options,
                tenantId,
                businessId,
                professionalId);
        }
    }

    [Fact]
    public async Task ProfessionalWriteService_ShouldNotFindOtherTenantResources()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();

        try
        {
            await using (var setupContext = new AppDbContext(
                options,
                CreateTenantContext(tenantBId)))
            {
                setupContext.Tenants.AddRange(
                    new Tenant(tenantAId, "Tenant A"),
                    new Tenant(tenantBId, "Tenant B"));

                setupContext.Businesses.Add(
                    new Business(
                        businessBId,
                        tenantBId,
                        BusinessTypeIds.Barbershop,
                        "Business B",
                        CreatedAt));

                setupContext.Professionals.Add(
                    new Professional(
                        professionalBId,
                        tenantBId,
                        businessBId,
                        "Professional B",
                        CreatedAt));

                await setupContext.SaveChangesAsync();
            }

            await using (var contextA = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                var writeService = new ProfessionalWriteService(contextA);

                Assert.False(
                    await writeService.BusinessExistsAsync(businessBId));

                Assert.Null(
                    await writeService.GetByIdAsync(professionalBId));
            }

            await using (var contextB = new AppDbContext(
                options,
                CreateTenantContext(tenantBId)))
            {
                var writeService = new ProfessionalWriteService(contextB);

                Assert.True(
                    await writeService.BusinessExistsAsync(businessBId));

                var professional =
                    await writeService.GetByIdAsync(professionalBId);

                Assert.NotNull(professional);
                Assert.Equal(professionalBId, professional.Id);
                Assert.Equal(tenantBId, professional.TenantId);
                Assert.Equal(businessBId, professional.BusinessId);
            }
        }
        finally
        {
            try
            {
                await RemoveTestDataAsync(
                    options,
                    tenantBId,
                    businessBId,
                    professionalBId);
            }
            finally
            {
                await RemoveTestDataAsync(
                    options,
                    tenantAId,
                    businessBId,
                    professionalBId);
            }
        }
    }

    [Theory]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task SaveChanges_WithProfessionalFromAnotherTenant_ShouldThrow(
        EntityState state)
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        await using var context = new AppDbContext(
            options,
            CreateTenantContext(tenantAId));

        var professionalFromTenantB = new Professional(
            Guid.NewGuid(),
            tenantBId,
            Guid.NewGuid(),
            "Professional B",
            CreatedAt);

        context.Entry(professionalFromTenantB).State = state;

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.SaveChangesAsync());

        Assert.Equal(
            "Cross-tenant data modification is not allowed.",
            exception.Message);
    }

    [Fact]
    public async Task ProfessionalReadService_ShouldReturnOnlyCurrentTenantData()
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalAId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();

        try
        {
            await using (var setupA = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                setupA.Tenants.Add(new Tenant(tenantAId, "Tenant A"));

                setupA.Businesses.Add(
                    new Business(
                        businessAId,
                        tenantAId,
                        BusinessTypeIds.Barbershop,
                        "Business A",
                        CreatedAt));

                var professionalA = new Professional(
                    professionalAId,
                    tenantAId,
                    businessAId,
                    "Professional A",
                    CreatedAt);

                professionalA.Update(
                    "Professional A",
                    false,
                    CreatedAt.AddHours(1));

                setupA.Professionals.Add(professionalA);

                await setupA.SaveChangesAsync();
            }

            await using (var setupB = new AppDbContext(
                options,
                CreateTenantContext(tenantBId)))
            {
                setupB.Tenants.Add(new Tenant(tenantBId, "Tenant B"));

                setupB.Businesses.Add(
                    new Business(
                        businessBId,
                        tenantBId,
                        BusinessTypeIds.Barbershop,
                        "Business B",
                        CreatedAt));

                setupB.Professionals.Add(
                    new Professional(
                        professionalBId,
                        tenantBId,
                        businessBId,
                        "Professional B",
                        CreatedAt));

                await setupB.SaveChangesAsync();
            }

            await using (var contextA = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                var readService = new ProfessionalReadService(contextA);

                var professionals = await readService.GetAllAsync();
                var response = Assert.Single(professionals);

                Assert.Equal(professionalAId, response.Id);
                Assert.Equal(businessAId, response.BusinessId);
                Assert.Equal("Professional A", response.Name);
                Assert.False(response.IsActive);

                var byId = await readService.GetByIdAsync(professionalAId);

                Assert.NotNull(byId);
                Assert.Equal(response, byId);

                Assert.Null(
                    await readService.GetByIdAsync(professionalBId));

                Assert.Null(
                    await readService.GetByIdAsync(Guid.NewGuid()));
            }

            await using (var contextB = new AppDbContext(
                options,
                CreateTenantContext(tenantBId)))
            {
                var readService = new ProfessionalReadService(contextB);

                var professionals = await readService.GetAllAsync();
                var response = Assert.Single(professionals);

                Assert.Equal(professionalBId, response.Id);
                Assert.Equal(businessBId, response.BusinessId);
                Assert.Equal("Professional B", response.Name);
                Assert.True(response.IsActive);

                var byId = await readService.GetByIdAsync(professionalBId);

                Assert.NotNull(byId);
                Assert.Equal(response, byId);

                Assert.Null(
                    await readService.GetByIdAsync(professionalAId));
            }
        }
        finally
        {
            try
            {
                await RemoveTestDataAsync(
                    options,
                    tenantAId,
                    businessAId,
                    professionalAId);
            }
            finally
            {
                await RemoveTestDataAsync(
                    options,
                    tenantBId,
                    businessBId,
                    professionalBId);
            }
        }
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task SaveChanges_WithForgedTenantId_ShouldPreserveOtherTenantProfessional(
        EntityState state)
    {
        var options = CreateOptions();

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();

        var professionalBId = Guid.NewGuid();

        try
        {
            await using (var setupA = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                setupA.Tenants.Add(new Tenant(tenantAId, "Tenant A"));

                setupA.Businesses.Add(
                    new Business(
                        businessAId,
                        tenantAId,
                        BusinessTypeIds.Barbershop,
                        "Business A",
                        CreatedAt));

                await setupA.SaveChangesAsync();
            }

            await using (var setupB = new AppDbContext(
                options,
                CreateTenantContext(tenantBId)))
            {
                setupB.Tenants.Add(new Tenant(tenantBId, "Tenant B"));

                setupB.Businesses.Add(
                    new Business(
                        businessBId,
                        tenantBId,
                        BusinessTypeIds.Barbershop,
                        "Business B",
                        CreatedAt));

                setupB.Professionals.Add(
                    new Professional(
                        professionalBId,
                        tenantBId,
                        businessBId,
                        "Professional B",
                        CreatedAt));

                await setupB.SaveChangesAsync();
            }

            await using (var attackContext = new AppDbContext(
                options,
                CreateTenantContext(tenantAId)))
            {
                var forgedProfessional = new Professional(
                    professionalBId,
                    tenantAId,
                    businessAId,
                    "Forged Professional",
                    CreatedAt);

                attackContext.Entry(forgedProfessional).State = state;

                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                    () => attackContext.SaveChangesAsync());
            }

            await using (var verificationContext = new AppDbContext(
                options,
                CreateTenantContext(tenantBId)))
            {
                var professional = await verificationContext.Professionals
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == professionalBId);

                Assert.Equal(tenantBId, professional.TenantId);
                Assert.Equal(businessBId, professional.BusinessId);
                Assert.Equal("Professional B", professional.Name);
                Assert.True(professional.IsActive);
                Assert.Equal(CreatedAt, professional.CreatedAt);
                Assert.Equal(CreatedAt, professional.UpdatedAt);
            }
        }
        finally
        {
            try
            {
                await RemoveTestDataAsync(
                    options,
                    tenantBId,
                    businessBId,
                    professionalBId);
            }
            finally
            {
                await RemoveTestDataAsync(
                    options,
                    tenantAId,
                    businessAId,
                    professionalBId);
            }
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
        var context = new TenantContext();

        context.Initialize(tenantId);

        return context;
    }

    private static async Task RemoveTestDataAsync(
        DbContextOptions<AppDbContext> options,
        Guid tenantId,
        Guid businessId,
        Guid professionalId)
    {
        await using var context = new AppDbContext(
            options,
            CreateTenantContext(tenantId));

        var professional = await context.Professionals
            .SingleOrDefaultAsync(x => x.Id == professionalId);

        if (professional is not null)
        {
            context.Professionals.Remove(professional);
            await context.SaveChangesAsync();
        }

        var business = await context.Businesses
            .SingleOrDefaultAsync(x => x.Id == businessId);

        if (business is not null)
        {
            context.Businesses.Remove(business);
            await context.SaveChangesAsync();
        }

        var tenant = await context.Tenants
            .SingleOrDefaultAsync(x => x.Id == tenantId);

        if (tenant is not null)
        {
            context.Tenants.Remove(tenant);
            await context.SaveChangesAsync();
        }
    }
}