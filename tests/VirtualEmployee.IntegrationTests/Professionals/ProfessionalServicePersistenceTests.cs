using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.Infrastructure.ProfessionalServices;
using VirtualEmployee.IntegrationTests.Infrastructure;

namespace VirtualEmployee.IntegrationTests.Professionals;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProfessionalServicePersistenceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public ProfessionalServicePersistenceTests(
        PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProfessionalService_ShouldPersistAndLoadOperationalData()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                serviceId);

            await using (var writeContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                writeContext.ProfessionalServices.Add(
                    new ProfessionalService(
                        tenantId,
                        professionalId,
                        serviceId,
                        CreatedAt));

                await writeContext.SaveChangesAsync();
            }

            await using var readContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var link = await readContext.ProfessionalServices
                .AsNoTracking()
                .SingleAsync(x =>
                    x.ProfessionalId == professionalId &&
                    x.ServiceId == serviceId);

            Assert.Equal(tenantId, link.TenantId);
            Assert.Equal(professionalId, link.ProfessionalId);
            Assert.Equal(serviceId, link.ServiceId);
            Assert.True(link.IsActive);
            Assert.Equal(CreatedAt, link.CreatedAt);
            Assert.Equal(CreatedAt, link.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalService_ShouldPersistDeactivationAndReactivation()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        var deactivatedAt = CreatedAt.AddHours(1);
        var reactivatedAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                serviceId);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.ProfessionalServices.Add(
                    new ProfessionalService(
                        tenantId,
                        professionalId,
                        serviceId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var link = await context.ProfessionalServices
                    .SingleAsync(x =>
                        x.ProfessionalId == professionalId &&
                        x.ServiceId == serviceId);

                link.Update(false, deactivatedAt);

                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var link = await context.ProfessionalServices
                    .SingleAsync(x =>
                        x.ProfessionalId == professionalId &&
                        x.ServiceId == serviceId);

                Assert.False(link.IsActive);
                Assert.Equal(CreatedAt, link.CreatedAt);
                Assert.Equal(deactivatedAt, link.UpdatedAt);

                link.Update(true, reactivatedAt);

                await context.SaveChangesAsync();
            }

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var persistedLink = await verificationContext.ProfessionalServices
                .AsNoTracking()
                .SingleAsync(x =>
                    x.ProfessionalId == professionalId &&
                    x.ServiceId == serviceId);

            Assert.Equal(tenantId, persistedLink.TenantId);
            Assert.Equal(professionalId, persistedLink.ProfessionalId);
            Assert.Equal(serviceId, persistedLink.ServiceId);
            Assert.True(persistedLink.IsActive);
            Assert.Equal(CreatedAt, persistedLink.CreatedAt);
            Assert.Equal(reactivatedAt, persistedLink.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalService_ShouldFilterQueriesByCurrentTenant()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalAId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantAId,
                businessAId,
                professionalAId,
                serviceAId);

            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                serviceBId);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId)))
            {
                context.ProfessionalServices.Add(
                    new ProfessionalService(
                        tenantAId,
                        professionalAId,
                        serviceAId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantBId)))
            {
                context.ProfessionalServices.Add(
                    new ProfessionalService(
                        tenantBId,
                        professionalBId,
                        serviceBId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId)))
            {
                var links = await context.ProfessionalServices
                    .AsNoTracking()
                    .Where(x =>
                        x.ProfessionalId == professionalAId ||
                        x.ProfessionalId == professionalBId)
                    .ToListAsync();

                var link = Assert.Single(links);

                Assert.Equal(tenantAId, link.TenantId);
                Assert.Equal(professionalAId, link.ProfessionalId);
                Assert.Equal(serviceAId, link.ServiceId);

                Assert.False(
                    await context.ProfessionalServices.AnyAsync(x =>
                        x.ProfessionalId == professionalBId &&
                        x.ServiceId == serviceBId));

                // Confirma que os dois registros existem no banco.
                var unfilteredLinks = await context.ProfessionalServices
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x =>
                        x.ProfessionalId == professionalAId ||
                        x.ProfessionalId == professionalBId)
                    .ToListAsync();

                Assert.Equal(2, unfilteredLinks.Count);
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantBId)))
            {
                var links = await context.ProfessionalServices
                    .AsNoTracking()
                    .Where(x =>
                        x.ProfessionalId == professionalAId ||
                        x.ProfessionalId == professionalBId)
                    .ToListAsync();

                var link = Assert.Single(links);

                Assert.Equal(tenantBId, link.TenantId);
                Assert.Equal(professionalBId, link.ProfessionalId);
                Assert.Equal(serviceBId, link.ServiceId);

                Assert.False(
                    await context.ProfessionalServices.AnyAsync(x =>
                        x.ProfessionalId == professionalAId &&
                        x.ServiceId == serviceAId));
            }
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Fact]
    public async Task ProfessionalService_WithDuplicateLink_ShouldRejectInsert()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                serviceId);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.ProfessionalServices.Add(
                    new ProfessionalService(
                        tenantId,
                        professionalId,
                        serviceId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.ProfessionalServices.Add(
                    new ProfessionalService(
                        tenantId,
                        professionalId,
                        serviceId,
                        CreatedAt.AddHours(1)));

                var exception = await Assert.ThrowsAsync<DbUpdateException>(
                    () => context.SaveChangesAsync());

                var postgresException = Assert.IsType<Npgsql.PostgresException>(
                    exception.InnerException);

                Assert.Equal(
                    Npgsql.PostgresErrorCodes.UniqueViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    "PK_professional_services",
                    postgresException.ConstraintName);
            }

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var links = await verificationContext.ProfessionalServices
                .AsNoTracking()
                .Where(x =>
                    x.ProfessionalId == professionalId &&
                    x.ServiceId == serviceId)
                .ToListAsync();

            var persistedLink = Assert.Single(links);

            Assert.Equal(tenantId, persistedLink.TenantId);
            Assert.True(persistedLink.IsActive);
            Assert.Equal(CreatedAt, persistedLink.CreatedAt);
            Assert.Equal(CreatedAt, persistedLink.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalServiceWriteService_ShouldReplaceLinksAndPreserveDates()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        var replacedAt = CreatedAt.AddHours(1);
        var unchangedAt = CreatedAt.AddHours(2);
        var reactivatedAt = CreatedAt.AddHours(3);

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                serviceAId);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Services.Add(
                    new Service(
                        serviceBId,
                        tenantId,
                        businessId,
                        "Service B",
                        ServiceType.Single,
                        150m,
                        90,
                        CreatedAt));

                await context.SaveChangesAsync();

                var writeService = new ProfessionalServiceWriteService(context);

                await writeService.ReplaceAsync(
                    professionalId,
                    [serviceAId],
                    CreatedAt);

                await writeService.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var initialLink = await context.ProfessionalServices
                    .AsNoTracking()
                    .SingleAsync(x => x.ProfessionalId == professionalId);

                Assert.Equal(serviceAId, initialLink.ServiceId);
                Assert.True(initialLink.IsActive);
                Assert.Equal(CreatedAt, initialLink.CreatedAt);
                Assert.Equal(CreatedAt, initialLink.UpdatedAt);

                var writeService = new ProfessionalServiceWriteService(context);

                await writeService.ReplaceAsync(
                    professionalId,
                    [serviceBId],
                    replacedAt);

                await writeService.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var links = await context.ProfessionalServices
                    .AsNoTracking()
                    .Where(x => x.ProfessionalId == professionalId)
                    .ToListAsync();

                Assert.Equal(2, links.Count);

                var linkA = Assert.Single(
                    links,
                    x => x.ServiceId == serviceAId);

                var linkB = Assert.Single(
                    links,
                    x => x.ServiceId == serviceBId);

                Assert.False(linkA.IsActive);
                Assert.Equal(CreatedAt, linkA.CreatedAt);
                Assert.Equal(replacedAt, linkA.UpdatedAt);

                Assert.True(linkB.IsActive);
                Assert.Equal(replacedAt, linkB.CreatedAt);
                Assert.Equal(replacedAt, linkB.UpdatedAt);

                var writeService = new ProfessionalServiceWriteService(context);

                // Repetir o conjunto não deve alterar as datas.
                await writeService.ReplaceAsync(
                    professionalId,
                    [serviceBId],
                    unchangedAt);

                await writeService.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var links = await context.ProfessionalServices
                    .AsNoTracking()
                    .Where(x => x.ProfessionalId == professionalId)
                    .ToListAsync();

                Assert.Equal(2, links.Count);

                Assert.All(
                    links,
                    link => Assert.Equal(replacedAt, link.UpdatedAt));

                var writeService = new ProfessionalServiceWriteService(context);

                await writeService.ReplaceAsync(
                    professionalId,
                    [serviceAId, serviceBId],
                    reactivatedAt);

                await writeService.SaveChangesAsync();
            }

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var finalLinks = await verificationContext.ProfessionalServices
                .AsNoTracking()
                .Where(x => x.ProfessionalId == professionalId)
                .ToListAsync();

            Assert.Equal(2, finalLinks.Count);
            Assert.All(finalLinks, link =>
            {
                Assert.Equal(tenantId, link.TenantId);
                Assert.True(link.IsActive);
            });

            var finalLinkA = Assert.Single(
                finalLinks,
                x => x.ServiceId == serviceAId);

            var finalLinkB = Assert.Single(
                finalLinks,
                x => x.ServiceId == serviceBId);

            Assert.Equal(CreatedAt, finalLinkA.CreatedAt);
            Assert.Equal(reactivatedAt, finalLinkA.UpdatedAt);

            Assert.Equal(replacedAt, finalLinkB.CreatedAt);
            Assert.Equal(replacedAt, finalLinkB.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalServiceWriteService_WithEmptyList_ShouldDeactivateAllLinks()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        var deactivatedAt = CreatedAt.AddHours(1);
        var repeatedAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                serviceId);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var writeService = new ProfessionalServiceWriteService(context);

                await writeService.ReplaceAsync(
                    professionalId,
                    [serviceId],
                    CreatedAt);

                await writeService.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var writeService = new ProfessionalServiceWriteService(context);

                await writeService.ReplaceAsync(
                    professionalId,
                    [],
                    deactivatedAt);

                await writeService.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var link = await context.ProfessionalServices
                    .AsNoTracking()
                    .SingleAsync(x =>
                        x.ProfessionalId == professionalId &&
                        x.ServiceId == serviceId);

                Assert.False(link.IsActive);
                Assert.Equal(CreatedAt, link.CreatedAt);
                Assert.Equal(deactivatedAt, link.UpdatedAt);

                var writeService = new ProfessionalServiceWriteService(context);

                await writeService.ReplaceAsync(
                    professionalId,
                    [],
                    repeatedAt);

                await writeService.SaveChangesAsync();
            }

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var links = await verificationContext.ProfessionalServices
                .AsNoTracking()
                .Where(x => x.ProfessionalId == professionalId)
                .ToListAsync();

            var persistedLink = Assert.Single(links);

            Assert.Equal(tenantId, persistedLink.TenantId);
            Assert.Equal(serviceId, persistedLink.ServiceId);
            Assert.False(persistedLink.IsActive);
            Assert.Equal(CreatedAt, persistedLink.CreatedAt);
            Assert.Equal(deactivatedAt, persistedLink.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task ProfessionalService_WithAnotherTenantOwnership_ShouldRejectWrite(
        EntityState state)
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                serviceBId);

            // Atualização e exclusão precisam de um vínculo existente.
            if (state != EntityState.Added)
            {
                await using var seedContext = new AppDbContext(
                    CreateOptions(),
                    CreateTenantContext(tenantBId));

                seedContext.ProfessionalServices.Add(
                    new ProfessionalService(
                        tenantBId,
                        professionalBId,
                        serviceBId,
                        CreatedAt));

                await seedContext.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId)))
            {
                var link = new ProfessionalService(
                    tenantBId,
                    professionalBId,
                    serviceBId,
                    CreatedAt);

                if (state == EntityState.Modified)
                {
                    link.Update(false, CreatedAt.AddHours(1));
                }

                context.Entry(link).State = state;

                var exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => context.SaveChangesAsync());

                Assert.Equal(
                    "Cross-tenant data modification is not allowed.",
                    exception.Message);
            }

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantBId));

            var persistedLink = await verificationContext.ProfessionalServices
                .AsNoTracking()
                .SingleOrDefaultAsync(x =>
                    x.ProfessionalId == professionalBId &&
                    x.ServiceId == serviceBId);

            if (state == EntityState.Added)
            {
                Assert.Null(persistedLink);
            }
            else
            {
                Assert.NotNull(persistedLink);
                Assert.Equal(tenantBId, persistedLink.TenantId);
                Assert.Equal(professionalBId, persistedLink.ProfessionalId);
                Assert.Equal(serviceBId, persistedLink.ServiceId);
                Assert.True(persistedLink.IsActive);
                Assert.Equal(CreatedAt, persistedLink.CreatedAt);
                Assert.Equal(CreatedAt, persistedLink.UpdatedAt);
            }
        }
        finally
        {
            await CleanupAsync(tenantBId);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProfessionalService_WithCrossTenantReference_ShouldRejectInsert(
        bool useOtherTenantProfessional)
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalAId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();
        var serviceAId = Guid.NewGuid();
        var serviceBId = Guid.NewGuid();

        var professionalId = useOtherTenantProfessional
            ? professionalBId
            : professionalAId;

        var serviceId = useOtherTenantProfessional
            ? serviceAId
            : serviceBId;

        var expectedConstraint = useOtherTenantProfessional
            ? "FK_professional_services_professionals_tenant_id_professional_~"
            : "FK_professional_services_services_tenant_id_service_id";

        try
        {
            await SeedAsync(
                tenantAId,
                businessAId,
                professionalAId,
                serviceAId);

            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                serviceBId);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId)))
            {
                context.ProfessionalServices.Add(
                    new ProfessionalService(
                        tenantAId,
                        professionalId,
                        serviceId,
                        CreatedAt));

                var exception = await Assert.ThrowsAsync<DbUpdateException>(
                    () => context.SaveChangesAsync());

                var postgresException = Assert.IsType<Npgsql.PostgresException>(
                    exception.InnerException);

                Assert.Equal(
                    Npgsql.PostgresErrorCodes.ForeignKeyViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    expectedConstraint,
                    postgresException.ConstraintName);
            }

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId));

            Assert.False(
                await verificationContext.ProfessionalServices
                    .IgnoreQueryFilters()
                    .AnyAsync(x =>
                        x.TenantId == tenantAId ||
                        x.TenantId == tenantBId));
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProfessionalService_ShouldPreventDeletionOfLinkedEntity(
        bool deleteProfessional)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        var expectedConstraint = deleteProfessional
            ? "FK_professional_services_professionals_tenant_id_professional_~"
            : "FK_professional_services_services_tenant_id_service_id";

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                serviceId);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.ProfessionalServices.Add(
                    new ProfessionalService(
                        tenantId,
                        professionalId,
                        serviceId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                if (deleteProfessional)
                {
                    var professional = await context.Professionals
                        .SingleAsync(x => x.Id == professionalId);

                    context.Professionals.Remove(professional);
                }
                else
                {
                    var service = await context.Services
                        .SingleAsync(x => x.Id == serviceId);

                    context.Services.Remove(service);
                }

                var exception = await Assert.ThrowsAsync<DbUpdateException>(
                    () => context.SaveChangesAsync());

                var postgresException = Assert.IsType<Npgsql.PostgresException>(
                    exception.InnerException);

                Assert.Equal(
                    Npgsql.PostgresErrorCodes.RestrictViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    expectedConstraint,
                    postgresException.ConstraintName);
            }

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            Assert.True(
                await verificationContext.Professionals
                    .AnyAsync(x => x.Id == professionalId));

            Assert.True(
                await verificationContext.Services
                    .AnyAsync(x => x.Id == serviceId));

            var link = await verificationContext.ProfessionalServices
                .AsNoTracking()
                .SingleAsync(x =>
                    x.ProfessionalId == professionalId &&
                    x.ServiceId == serviceId);

            Assert.Equal(tenantId, link.TenantId);
            Assert.True(link.IsActive);
            Assert.Equal(CreatedAt, link.CreatedAt);
            Assert.Equal(CreatedAt, link.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }
    private async Task SeedAsync(
        Guid tenantId,
        Guid businessId,
        Guid professionalId,
        Guid serviceId)
    {
        await using var context = new AppDbContext(
            CreateOptions(),
            CreateTenantContext(tenantId));

        context.Tenants.Add(
            new Tenant(
                tenantId,
                "Professional Service Tenant"));

        context.Businesses.Add(
            new Business(
                businessId,
                tenantId,
                BusinessTypeIds.Barbershop,
                "Professional Service Business",
                CreatedAt));

        await context.SaveChangesAsync();

        context.Professionals.Add(
            new Professional(
                professionalId,
                tenantId,
                businessId,
                "Professional",
                CreatedAt));

        context.Services.Add(
            new Service(
                serviceId,
                tenantId,
                businessId,
                "Service",
                ServiceType.Single,
                100m,
                60,
                CreatedAt));

        await context.SaveChangesAsync();
    }

    private async Task CleanupAsync(Guid tenantId)
    {
        await using var context = new AppDbContext(
            CreateOptions(),
            CreateTenantContext(tenantId));

        var links = await context.ProfessionalServices
            .Where(x => x.TenantId == tenantId)
            .ToListAsync();

        context.ProfessionalServices.RemoveRange(links);
        await context.SaveChangesAsync();

        var professionals = await context.Professionals
            .Where(x => x.TenantId == tenantId)
            .ToListAsync();

        var services = await context.Services
            .Where(x => x.TenantId == tenantId)
            .ToListAsync();

        context.Professionals.RemoveRange(professionals);
        context.Services.RemoveRange(services);
        await context.SaveChangesAsync();

        var businesses = await context.Businesses
            .Where(x => x.TenantId == tenantId)
            .ToListAsync();

        context.Businesses.RemoveRange(businesses);
        await context.SaveChangesAsync();

        var tenants = await context.Tenants
            .Where(x => x.Id == tenantId)
            .ToListAsync();

        context.Tenants.RemoveRange(tenants);
        await context.SaveChangesAsync();
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
}