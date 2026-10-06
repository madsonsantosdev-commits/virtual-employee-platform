using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.Infrastructure.ProfessionalLocations;
using VirtualEmployee.IntegrationTests.Infrastructure;

namespace VirtualEmployee.IntegrationTests.Professionals;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProfessionalLocationPersistenceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public ProfessionalLocationPersistenceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProfessionalLocation_ShouldPersistAndLoadOperationalData()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                locationId);

            await using (var writeContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                writeContext.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantId,
                        professionalId,
                        locationId,
                        CreatedAt));

                await writeContext.SaveChangesAsync();
            }

            await using var readContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var link = await readContext.ProfessionalLocations
                .AsNoTracking()
                .SingleAsync(x =>
                    x.ProfessionalId == professionalId &&
                    x.LocationId == locationId);

            Assert.Equal(tenantId, link.TenantId);
            Assert.Equal(professionalId, link.ProfessionalId);
            Assert.Equal(locationId, link.LocationId);
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
        Guid locationId)
    {
        await using var context = new AppDbContext(
            CreateOptions(),
            CreateTenantContext(tenantId));

        context.Tenants.Add(
            new Tenant(tenantId, "Professional Location Tenant"));

        context.Businesses.Add(
            new Business(
                businessId,
                tenantId,
                BusinessTypeIds.Barbershop,
                "Professional Location Business",
                CreatedAt));

        await context.SaveChangesAsync();

        context.Professionals.Add(
            new Professional(
                professionalId,
                tenantId,
                businessId,
                "Professional",
                CreatedAt));

        context.Locations.Add(
            new Location(
                locationId,
                tenantId,
                businessId,
                "Location",
                "BR",
                "America/Sao_Paulo",
                CreatedAt));

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task ProfessionalLocation_ShouldPersistDeactivationAndReactivation()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var deactivatedAt = CreatedAt.AddHours(1);
        var reactivatedAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                locationId);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantId,
                        professionalId,
                        locationId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var link = await context.ProfessionalLocations
                    .SingleAsync(x =>
                        x.ProfessionalId == professionalId &&
                        x.LocationId == locationId);

                link.Update(false, deactivatedAt);

                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var link = await context.ProfessionalLocations
                    .SingleAsync(x =>
                        x.ProfessionalId == professionalId &&
                        x.LocationId == locationId);

                Assert.False(link.IsActive);
                Assert.Equal(deactivatedAt, link.UpdatedAt);
                Assert.Equal(CreatedAt, link.CreatedAt);
                Assert.Equal(tenantId, link.TenantId);
                Assert.Equal(professionalId, link.ProfessionalId);
                Assert.Equal(locationId, link.LocationId);

                link.Update(true, reactivatedAt);

                await context.SaveChangesAsync();
            }

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var persistedLink = await verificationContext.ProfessionalLocations
                .AsNoTracking()
                .SingleAsync(x =>
                    x.ProfessionalId == professionalId &&
                    x.LocationId == locationId);

            Assert.True(persistedLink.IsActive);
            Assert.Equal(reactivatedAt, persistedLink.UpdatedAt);
            Assert.Equal(CreatedAt, persistedLink.CreatedAt);
            Assert.Equal(tenantId, persistedLink.TenantId);
            Assert.Equal(professionalId, persistedLink.ProfessionalId);
            Assert.Equal(locationId, persistedLink.LocationId);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalLocation_ShouldIsolateQueriesByTenant()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalAId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();
        var locationAId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantAId,
                businessAId,
                professionalAId,
                locationAId);

            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                locationBId);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId)))
            {
                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantAId,
                        professionalAId,
                        locationAId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantBId)))
            {
                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantBId,
                        professionalBId,
                        locationBId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId)))
            {
                var physicalLinks = await context.ProfessionalLocations
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x =>
                        x.TenantId == tenantAId ||
                        x.TenantId == tenantBId)
                    .ToListAsync();

                Assert.Equal(2, physicalLinks.Count);

                Assert.Contains(
                    physicalLinks,
                    x => x.TenantId == tenantAId &&
                        x.ProfessionalId == professionalAId &&
                        x.LocationId == locationAId);

                Assert.Contains(
                    physicalLinks,
                    x => x.TenantId == tenantBId &&
                        x.ProfessionalId == professionalBId &&
                        x.LocationId == locationBId);

                var visibleLinks = await context.ProfessionalLocations
                    .AsNoTracking()
                    .ToListAsync();

                var link = Assert.Single(visibleLinks);

                Assert.Equal(tenantAId, link.TenantId);
                Assert.Equal(professionalAId, link.ProfessionalId);
                Assert.Equal(locationAId, link.LocationId);

                var otherTenantLink = await context.ProfessionalLocations
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x =>
                        x.ProfessionalId == professionalBId &&
                        x.LocationId == locationBId);

                Assert.Null(otherTenantLink);
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantBId)))
            {
                var visibleLinks = await context.ProfessionalLocations
                    .AsNoTracking()
                    .ToListAsync();

                var link = Assert.Single(visibleLinks);

                Assert.Equal(tenantBId, link.TenantId);
                Assert.Equal(professionalBId, link.ProfessionalId);
                Assert.Equal(locationBId, link.LocationId);

                var otherTenantLink = await context.ProfessionalLocations
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x =>
                        x.ProfessionalId == professionalAId &&
                        x.LocationId == locationAId);

                Assert.Null(otherTenantLink);
            }
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }
    private async Task CleanupAsync(Guid tenantId)
    {
        await using var context = new AppDbContext(
            CreateOptions(),
            CreateTenantContext(tenantId));

        // Each test owns a newly generated tenant.
        var links = await context.ProfessionalLocations
            .Where(x => x.TenantId == tenantId)
            .ToListAsync();

        context.ProfessionalLocations.RemoveRange(links);
        await context.SaveChangesAsync();

        var professionals = await context.Professionals
            .Where(x => x.TenantId == tenantId)
            .ToListAsync();

        context.Professionals.RemoveRange(professionals);
        await context.SaveChangesAsync();

        var locations = await context.Locations
            .Where(x => x.TenantId == tenantId)
            .ToListAsync();

        context.Locations.RemoveRange(locations);
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
    public async Task ProfessionalLocation_WithDuplicateKey_ShouldRejectInsert()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                locationId);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantId,
                        professionalId,
                        locationId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            // A separate context lets PostgreSQL detect the duplicate.
            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantId,
                        professionalId,
                        locationId,
                        CreatedAt.AddHours(1)));

                var exception = await Assert.ThrowsAsync<DbUpdateException>(
                    () => context.SaveChangesAsync());

                var postgresException = Assert.IsType<Npgsql.PostgresException>(
                    exception.InnerException);

                Assert.Equal(
                    Npgsql.PostgresErrorCodes.UniqueViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    "PK_professional_locations",
                    postgresException.ConstraintName);
            }

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var links = await verificationContext.ProfessionalLocations
                .AsNoTracking()
                .ToListAsync();

            var persistedLink = Assert.Single(links);

            Assert.Equal(tenantId, persistedLink.TenantId);
            Assert.Equal(professionalId, persistedLink.ProfessionalId);
            Assert.Equal(locationId, persistedLink.LocationId);
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
    public async Task ProfessionalLocationWriteService_ShouldReplaceAndPreserveExistingLinks()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var locationAId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        var replacedAt = CreatedAt.AddHours(1);
        var reactivatedAt = CreatedAt.AddHours(2);
        var repeatedAt = CreatedAt.AddHours(3);

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                locationAId);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.Locations.Add(
                    new Location(
                        locationBId,
                        tenantId,
                        businessId,
                        "Location B",
                        "BR",
                        "America/Sao_Paulo",
                        CreatedAt));

                await context.SaveChangesAsync();

                var service = new ProfessionalLocationWriteService(context);

                await service.ReplaceAsync(
                    professionalId,
                    [locationAId],
                    CreatedAt);

                await service.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var link = await context.ProfessionalLocations
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal(locationAId, link.LocationId);
                Assert.True(link.IsActive);
                Assert.Equal(CreatedAt, link.CreatedAt);
                Assert.Equal(CreatedAt, link.UpdatedAt);
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var service = new ProfessionalLocationWriteService(context);

                await service.ReplaceAsync(
                    professionalId,
                    [locationBId],
                    replacedAt);

                await service.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var links = await context.ProfessionalLocations
                    .AsNoTracking()
                    .ToListAsync();

                Assert.Equal(2, links.Count);

                var linkA = Assert.Single(
                    links,
                    link => link.LocationId == locationAId);

                var linkB = Assert.Single(
                    links,
                    link => link.LocationId == locationBId);

                Assert.False(linkA.IsActive);
                Assert.Equal(CreatedAt, linkA.CreatedAt);
                Assert.Equal(replacedAt, linkA.UpdatedAt);

                Assert.True(linkB.IsActive);
                Assert.Equal(replacedAt, linkB.CreatedAt);
                Assert.Equal(replacedAt, linkB.UpdatedAt);
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var service = new ProfessionalLocationWriteService(context);

                await service.ReplaceAsync(
                    professionalId,
                    [locationAId, locationBId],
                    reactivatedAt);

                await service.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var links = await context.ProfessionalLocations
                    .AsNoTracking()
                    .ToListAsync();

                Assert.Equal(2, links.Count);
                Assert.All(links, link => Assert.True(link.IsActive));

                var linkA = Assert.Single(
                    links,
                    link => link.LocationId == locationAId);

                var linkB = Assert.Single(
                    links,
                    link => link.LocationId == locationBId);

                Assert.Equal(CreatedAt, linkA.CreatedAt);
                Assert.Equal(reactivatedAt, linkA.UpdatedAt);
                Assert.Equal(replacedAt, linkB.CreatedAt);
                Assert.Equal(replacedAt, linkB.UpdatedAt);
            }

            // Repeating the same selection must preserve both timestamps.
            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var service = new ProfessionalLocationWriteService(context);

                await service.ReplaceAsync(
                    professionalId,
                    [locationAId, locationBId],
                    repeatedAt);

                await service.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var links = await context.ProfessionalLocations
                    .AsNoTracking()
                    .ToListAsync();

                Assert.Equal(2, links.Count);

                Assert.All(links, link =>
                {
                    Assert.Equal(tenantId, link.TenantId);
                    Assert.Equal(professionalId, link.ProfessionalId);
                    Assert.True(link.IsActive);
                });

                var linkA = Assert.Single(
                    links,
                    link => link.LocationId == locationAId);

                var linkB = Assert.Single(
                    links,
                    link => link.LocationId == locationBId);

                Assert.Equal(CreatedAt, linkA.CreatedAt);
                Assert.Equal(reactivatedAt, linkA.UpdatedAt);
                Assert.Equal(replacedAt, linkB.CreatedAt);
                Assert.Equal(replacedAt, linkB.UpdatedAt);
            }
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ProfessionalLocationWriteService_WithEmptyList_ShouldDeactivateAllLinks()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var deactivatedAt = CreatedAt.AddHours(1);

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                locationId);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var service = new ProfessionalLocationWriteService(context);

                await service.ReplaceAsync(
                    professionalId,
                    [locationId],
                    CreatedAt);

                await service.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                var service = new ProfessionalLocationWriteService(context);

                await service.ReplaceAsync(
                    professionalId,
                    [],
                    deactivatedAt);

                await service.SaveChangesAsync();
            }

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId));

            var link = await verificationContext.ProfessionalLocations
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(tenantId, link.TenantId);
            Assert.Equal(professionalId, link.ProfessionalId);
            Assert.Equal(locationId, link.LocationId);
            Assert.False(link.IsActive);
            Assert.Equal(CreatedAt, link.CreatedAt);
            Assert.Equal(deactivatedAt, link.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProfessionalLocation_ShouldPreventDeletionOfLinkedEntity(
        bool deleteProfessional)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var expectedConstraint = deleteProfessional
            ? "FK_professional_locations_professionals_tenant_id_professional~"
            : "FK_professional_locations_locations_tenant_id_location_id";

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                professionalId,
                locationId);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantId)))
            {
                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantId,
                        professionalId,
                        locationId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            // The link is not tracked here, so PostgreSQL enforces Restrict.
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
                    var location = await context.Locations
                        .SingleAsync(x => x.Id == locationId);

                    context.Locations.Remove(location);
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
                await verificationContext.Locations
                    .AnyAsync(x => x.Id == locationId));

            var link = await verificationContext.ProfessionalLocations
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(tenantId, link.TenantId);
            Assert.Equal(professionalId, link.ProfessionalId);
            Assert.Equal(locationId, link.LocationId);
            Assert.True(link.IsActive);
            Assert.Equal(CreatedAt, link.CreatedAt);
            Assert.Equal(CreatedAt, link.UpdatedAt);
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
    public async Task ProfessionalLocation_WithDifferentTenant_ShouldRejectWrite(
        EntityState state)
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                locationBId);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantBId)))
            {
                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantBId,
                        professionalBId,
                        locationBId,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId)))
            {
                var link = new ProfessionalLocation(
                    tenantBId,
                    professionalBId,
                    locationBId,
                    CreatedAt);

                if (state == EntityState.Modified)
                {
                    link.Update(false, CreatedAt.AddHours(1));
                }

                context.Entry(link).State = state;

                var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => context.SaveChangesAsync());

                Assert.Equal(
                    "Cross-tenant data modification is not allowed.",
                    exception.Message);
            }

            await using var verificationContext = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantBId));

            var persistedLink = await verificationContext.ProfessionalLocations
                .AsNoTracking()
                .SingleAsync(x =>
                    x.ProfessionalId == professionalBId &&
                    x.LocationId == locationBId);

            Assert.Equal(tenantBId, persistedLink.TenantId);
            Assert.Equal(professionalBId, persistedLink.ProfessionalId);
            Assert.Equal(locationBId, persistedLink.LocationId);
            Assert.True(persistedLink.IsActive);
            Assert.Equal(CreatedAt, persistedLink.CreatedAt);
            Assert.Equal(CreatedAt, persistedLink.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantBId);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProfessionalLocation_WithCrossTenantReference_ShouldRejectInsert(
        bool useOtherTenantProfessional)
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var professionalAId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();
        var locationAId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();

        var professionalId = useOtherTenantProfessional
            ? professionalBId
            : professionalAId;

        var locationId = useOtherTenantProfessional
            ? locationAId
            : locationBId;

        var expectedConstraint = useOtherTenantProfessional
            ? "FK_professional_locations_professionals_tenant_id_professional~"
            : "FK_professional_locations_locations_tenant_id_location_id";

        try
        {
            await SeedAsync(
                tenantAId,
                businessAId,
                professionalAId,
                locationAId);

            await SeedAsync(
                tenantBId,
                businessBId,
                professionalBId,
                locationBId);

            await using (var context = new AppDbContext(
                CreateOptions(),
                CreateTenantContext(tenantAId)))
            {
                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantAId,
                        professionalId,
                        locationId,
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
                await verificationContext.ProfessionalLocations
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