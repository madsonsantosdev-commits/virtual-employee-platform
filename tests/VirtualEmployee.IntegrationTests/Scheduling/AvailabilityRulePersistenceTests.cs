using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.IntegrationTests.Infrastructure;
using VirtualEmployee.Application.AvailabilityRules;
using VirtualEmployee.Infrastructure.AvailabilityRules;
using Xunit;
using Npgsql;

namespace VirtualEmployee.IntegrationTests.Scheduling;

[Collection(PostgreSqlCollection.Name)]
public sealed class AvailabilityRulePersistenceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public AvailabilityRulePersistenceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AvailabilityRule_ShouldPersistAndLoadLocalTimes()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(tenantId))
            {
                context.AvailabilityRules.Add(
                    new AvailabilityRule(
                        ruleId,
                        tenantId,
                        locationId,
                        professionalId,
                        DayOfWeek.Monday,
                        new TimeOnly(9, 15),
                        new TimeOnly(12, 30),
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var readContext = CreateContext(tenantId);

            var rule = await readContext.AvailabilityRules
                .AsNoTracking()
                .SingleAsync(x => x.Id == ruleId);

            Assert.Equal(tenantId, rule.TenantId);
            Assert.Equal(locationId, rule.LocationId);
            Assert.Equal(professionalId, rule.ProfessionalId);
            Assert.Equal(DayOfWeek.Monday, rule.DayOfWeek);
            Assert.Equal(new TimeOnly(9, 15), rule.StartTime);
            Assert.Equal(new TimeOnly(12, 30), rule.EndTime);
            Assert.True(rule.IsActive);
            Assert.Equal(CreatedAt, rule.CreatedAt);
            Assert.Equal(CreatedAt, rule.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task AvailabilityRule_ShouldBeInvisibleToAnotherTenant()
    {
        var ownerTenantId = Guid.NewGuid();
        var anotherTenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                ownerTenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(ownerTenantId))
            {
                context.AvailabilityRules.Add(
                    new AvailabilityRule(
                        ruleId,
                        ownerTenantId,
                        locationId,
                        professionalId,
                        DayOfWeek.Monday,
                        new TimeOnly(9, 0),
                        new TimeOnly(12, 0),
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(anotherTenantId))
            {
                var rule = await context.AvailabilityRules
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == ruleId);

                Assert.Null(rule);

                Assert.False(
                    await context.AvailabilityRules
                        .AnyAsync(x => x.Id == ruleId));
            }

            await using (var context = CreateContext(ownerTenantId))
            {
                Assert.True(
                    await context.AvailabilityRules
                        .AnyAsync(x => x.Id == ruleId));
            }
        }
        finally
        {
            await CleanupAsync(ownerTenantId);
        }
    }

    [Fact]
    public async Task AvailabilityRule_ShouldPreventDeletionOfReferencedLink()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(tenantId))
            {
                context.AvailabilityRules.Add(
                    new AvailabilityRule(
                        ruleId,
                        tenantId,
                        locationId,
                        professionalId,
                        DayOfWeek.Monday,
                        new TimeOnly(9, 0),
                        new TimeOnly(12, 0),
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(tenantId))
            {
                var link = await context.ProfessionalLocations
                    .SingleAsync(x =>
                        x.ProfessionalId == professionalId &&
                        x.LocationId == locationId);

                context.ProfessionalLocations.Remove(link);

                var exception =
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync());

                var postgresException =
                    Assert.IsType<PostgresException>(
                        exception.InnerException);

                Assert.Equal(
                    PostgresErrorCodes.RestrictViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    "FK_availability_rules_professional_locations_tenant_id_profess~",
                    postgresException.ConstraintName);
            }

            await using (var context = CreateContext(tenantId))
            {
                Assert.True(
                    await context.ProfessionalLocations.AnyAsync(x =>
                        x.ProfessionalId == professionalId &&
                        x.LocationId == locationId));

                Assert.True(
                    await context.AvailabilityRules
                        .AnyAsync(x => x.Id == ruleId));
            }
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task AvailabilityRule_ShouldReplaceAndReactivatePreservingDates()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();

        var morning = new AvailabilityRuleInput(
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            new TimeOnly(12, 0));

        var afternoon = new AvailabilityRuleInput(
            DayOfWeek.Monday,
            new TimeOnly(13, 0),
            new TimeOnly(18, 0));

        var replacedAt = CreatedAt.AddHours(1);
        var reactivatedAt = CreatedAt.AddHours(2);

        Guid morningId;
        Guid afternoonId;

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(tenantId))
            {
                var service = new AvailabilityRuleWriteService(context);

                await service.ReplaceAsync(
                    locationId,
                    professionalId,
                    [morning, afternoon],
                    CreatedAt);

                await service.SaveChangesAsync();
            }

            await using (var context = CreateContext(tenantId))
            {
                var rules = await context.AvailabilityRules
                    .AsNoTracking()
                    .ToListAsync();

                Assert.Equal(2, rules.Count);

                var morningRule = Assert.Single(
                    rules,
                    rule => rule.StartTime == morning.StartTime);

                var afternoonRule = Assert.Single(
                    rules,
                    rule => rule.StartTime == afternoon.StartTime);

                morningId = morningRule.Id;
                afternoonId = afternoonRule.Id;

                Assert.All(rules, rule =>
                {
                    Assert.True(rule.IsActive);
                    Assert.Equal(CreatedAt, rule.CreatedAt);
                    Assert.Equal(CreatedAt, rule.UpdatedAt);
                });
            }

            await using (var context = CreateContext(tenantId))
            {
                var service = new AvailabilityRuleWriteService(context);

                await service.ReplaceAsync(
                    locationId,
                    professionalId,
                    [afternoon],
                    replacedAt);

                await service.SaveChangesAsync();
            }

            await using (var context = CreateContext(tenantId))
            {
                var rules = await context.AvailabilityRules
                    .AsNoTracking()
                    .ToListAsync();

                Assert.Equal(2, rules.Count);

                var morningRule = Assert.Single(
                    rules,
                    rule => rule.Id == morningId);

                var afternoonRule = Assert.Single(
                    rules,
                    rule => rule.Id == afternoonId);

                Assert.False(morningRule.IsActive);
                Assert.Equal(CreatedAt, morningRule.CreatedAt);
                Assert.Equal(replacedAt, morningRule.UpdatedAt);

                Assert.True(afternoonRule.IsActive);
                Assert.Equal(CreatedAt, afternoonRule.CreatedAt);
                Assert.Equal(CreatedAt, afternoonRule.UpdatedAt);
            }

            await using (var context = CreateContext(tenantId))
            {
                var service = new AvailabilityRuleWriteService(context);

                await service.ReplaceAsync(
                    locationId,
                    professionalId,
                    [morning, afternoon],
                    reactivatedAt);

                await service.SaveChangesAsync();
            }

            await using (var context = CreateContext(tenantId))
            {
                var rules = await context.AvailabilityRules
                    .AsNoTracking()
                    .ToListAsync();

                Assert.Equal(2, rules.Count);

                var morningRule = Assert.Single(
                    rules,
                    rule => rule.Id == morningId);

                var afternoonRule = Assert.Single(
                    rules,
                    rule => rule.Id == afternoonId);

                Assert.True(morningRule.IsActive);
                Assert.Equal(CreatedAt, morningRule.CreatedAt);
                Assert.Equal(reactivatedAt, morningRule.UpdatedAt);

                Assert.True(afternoonRule.IsActive);
                Assert.Equal(CreatedAt, afternoonRule.CreatedAt);
                Assert.Equal(CreatedAt, afternoonRule.UpdatedAt);
            }
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task AvailabilityRule_WithEmptyReplacement_ShouldDeactivateWithoutDeleting()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var deactivatedAt = CreatedAt.AddHours(1);

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(tenantId))
            {
                var service = new AvailabilityRuleWriteService(context);

                await service.ReplaceAsync(
                    locationId,
                    professionalId,
                    [
                        new AvailabilityRuleInput(
                            DayOfWeek.Monday,
                            new TimeOnly(9, 0),
                            new TimeOnly(12, 0)),
                        new AvailabilityRuleInput(
                            DayOfWeek.Tuesday,
                            new TimeOnly(13, 0),
                            new TimeOnly(18, 0))
                    ],
                    CreatedAt);

                await service.SaveChangesAsync();
            }

            await using (var context = CreateContext(tenantId))
            {
                var service = new AvailabilityRuleWriteService(context);

                await service.ReplaceAsync(
                    locationId,
                    professionalId,
                    [],
                    deactivatedAt);

                await service.SaveChangesAsync();
            }

            await using (var context = CreateContext(tenantId))
            {
                var rules = await context.AvailabilityRules
                    .AsNoTracking()
                    .ToListAsync();

                Assert.Equal(2, rules.Count);

                Assert.All(rules, rule =>
                {
                    Assert.False(rule.IsActive);
                    Assert.Equal(CreatedAt, rule.CreatedAt);
                    Assert.Equal(deactivatedAt, rule.UpdatedAt);
                });

                Assert.False(
                    await context.AvailabilityRules
                        .AnyAsync(rule => rule.IsActive));
            }

            await using (var context = CreateContext(tenantId))
            {
                var service = new AvailabilityRuleWriteService(context);

                await service.ReplaceAsync(
                    locationId,
                    professionalId,
                    [],
                    CreatedAt.AddHours(2));

                await service.SaveChangesAsync();
            }

            await using (var context = CreateContext(tenantId))
            {
                var rules = await context.AvailabilityRules
                    .AsNoTracking()
                    .ToListAsync();

                Assert.Equal(2, rules.Count);

                Assert.All(rules, rule =>
                {
                    Assert.False(rule.IsActive);
                    Assert.Equal(CreatedAt, rule.CreatedAt);
                    Assert.Equal(deactivatedAt, rule.UpdatedAt);
                });
            }
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AvailabilityRule_ShouldRejectInvalidProfessionalLocation(
        bool referenceAnotherTenant)
    {
        var ownerTenantId = Guid.NewGuid();
        var anotherTenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                ownerTenantId,
                businessId,
                locationId,
                professionalId);

            var writeTenantId = referenceAnotherTenant
                ? anotherTenantId
                : ownerTenantId;

            var referencedLocationId = referenceAnotherTenant
                ? locationId
                : Guid.NewGuid();

            await using (var context = CreateContext(writeTenantId))
            {
                context.AvailabilityRules.Add(
                    new AvailabilityRule(
                        ruleId,
                        writeTenantId,
                        referencedLocationId,
                        professionalId,
                        DayOfWeek.Monday,
                        new TimeOnly(9, 0),
                        new TimeOnly(12, 0),
                        CreatedAt));

                var exception =
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync());

                var postgresException =
                    Assert.IsType<PostgresException>(
                        exception.InnerException);

                Assert.Equal(
                    PostgresErrorCodes.ForeignKeyViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    "availability_rules",
                    postgresException.TableName);

                Assert.Equal(
                    "FK_availability_rules_professional_locations_tenant_id_profess~",
                    postgresException.ConstraintName);
            }

            await using (var context = CreateContext(ownerTenantId))
            {
                Assert.True(
                    await context.ProfessionalLocations.AnyAsync(x =>
                        x.ProfessionalId == professionalId &&
                        x.LocationId == locationId));

                Assert.False(
                    await context.AvailabilityRules
                        .IgnoreQueryFilters()
                        .AnyAsync(x => x.Id == ruleId));
            }
        }
        finally
        {
            await CleanupAsync(ownerTenantId);
        }
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task AvailabilityRule_ShouldRejectCrossTenantWrites(
        string operation)
    {
        var ownerTenantId = Guid.NewGuid();
        var anotherTenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                ownerTenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(ownerTenantId))
            {
                context.AvailabilityRules.Add(
                    new AvailabilityRule(
                        ruleId,
                        ownerTenantId,
                        locationId,
                        professionalId,
                        DayOfWeek.Monday,
                        new TimeOnly(9, 0),
                        new TimeOnly(12, 0),
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            var attemptedId = operation == "insert"
                ? Guid.NewGuid()
                : ruleId;

            var attemptedRule = new AvailabilityRule(
                attemptedId,
                ownerTenantId,
                locationId,
                professionalId,
                DayOfWeek.Monday,
                new TimeOnly(9, 0),
                new TimeOnly(12, 0),
                CreatedAt);

            await using (var context = CreateContext(anotherTenantId))
            {
                if (operation == "insert")
                {
                    context.AvailabilityRules.Add(attemptedRule);
                }
                else if (operation == "update")
                {
                    context.AvailabilityRules.Attach(attemptedRule);

                    attemptedRule.Update(
                        DayOfWeek.Tuesday,
                        new TimeOnly(13, 0),
                        new TimeOnly(18, 0),
                        false,
                        CreatedAt.AddHours(1));
                }
                else
                {
                    context.AvailabilityRules.Remove(attemptedRule);
                }

                var exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        () => context.SaveChangesAsync());

                Assert.Equal(
                    "Cross-tenant data modification is not allowed.",
                    exception.Message);
            }

            await using (var context = CreateContext(ownerTenantId))
            {
                var persistedRule = await context.AvailabilityRules
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == ruleId);

                Assert.Equal(DayOfWeek.Monday, persistedRule.DayOfWeek);
                Assert.Equal(new TimeOnly(9, 0), persistedRule.StartTime);
                Assert.Equal(new TimeOnly(12, 0), persistedRule.EndTime);
                Assert.True(persistedRule.IsActive);
                Assert.Equal(CreatedAt, persistedRule.UpdatedAt);

                if (operation == "insert")
                {
                    Assert.False(
                        await context.AvailabilityRules
                            .AnyAsync(x => x.Id == attemptedId));
                }
            }
        }
        finally
        {
            await CleanupAsync(ownerTenantId);
        }
    }

    [Theory]
    [InlineData(-1, 9, 12, "CK_availability_rules_day_of_week")]
    [InlineData(7, 9, 12, "CK_availability_rules_day_of_week")]
    [InlineData(1, 9, 9, "CK_availability_rules_time_window")]
    [InlineData(1, 12, 9, "CK_availability_rules_time_window")]
    public async Task AvailabilityRule_ShouldEnforceDatabaseChecks(
        int day,
        int startHour,
        int endHour,
        string expectedConstraint)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(tenantId))
            {
                var rule = new AvailabilityRule(
                    ruleId,
                    tenantId,
                    locationId,
                    professionalId,
                    DayOfWeek.Monday,
                    new TimeOnly(9, 0),
                    new TimeOnly(12, 0),
                    CreatedAt);

                var entry = context.AvailabilityRules.Add(rule);

                entry.Property(x => x.DayOfWeek).CurrentValue =
                    (DayOfWeek)day;

                entry.Property(x => x.StartTime).CurrentValue =
                    new TimeOnly(startHour, 0);

                entry.Property(x => x.EndTime).CurrentValue =
                    new TimeOnly(endHour, 0);

                var exception =
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync());

                var postgresException =
                    Assert.IsType<PostgresException>(
                        exception.InnerException);

                Assert.Equal(
                    PostgresErrorCodes.CheckViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    expectedConstraint,
                    postgresException.ConstraintName);
            }

            await using (var context = CreateContext(tenantId))
            {
                Assert.False(
                    await context.AvailabilityRules
                        .AnyAsync(x => x.Id == ruleId));
            }
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

    private async Task SeedAsync(
        Guid tenantId,
        Guid businessId,
        Guid locationId,
        Guid professionalId)
    {
        await using var context = CreateContext(tenantId);

        context.Tenants.Add(
            new Tenant(tenantId, "Availability Tenant"));

        context.Businesses.Add(
            new Business(
                businessId,
                tenantId,
                BusinessTypeIds.Barbershop,
                "Availability Business",
                CreatedAt));

        await context.SaveChangesAsync();

        context.Locations.Add(
            new Location(
                locationId,
                tenantId,
                businessId,
                "Location",
                "BR",
                "America/Sao_Paulo",
                CreatedAt));

        context.Professionals.Add(
            new Professional(
                professionalId,
                tenantId,
                businessId,
                "Professional",
                CreatedAt));

        await context.SaveChangesAsync();

        context.ProfessionalLocations.Add(
            new ProfessionalLocation(
                tenantId,
                professionalId,
                locationId,
                CreatedAt));

        await context.SaveChangesAsync();
    }

    private async Task CleanupAsync(Guid tenantId)
    {
        await using var context = CreateContext(tenantId);

        context.AvailabilityRules.RemoveRange(
            await context.AvailabilityRules.ToListAsync());
        await context.SaveChangesAsync();

        context.ProfessionalLocations.RemoveRange(
            await context.ProfessionalLocations.ToListAsync());
        await context.SaveChangesAsync();

        context.Professionals.RemoveRange(
            await context.Professionals.ToListAsync());

        context.Locations.RemoveRange(
            await context.Locations.ToListAsync());
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