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
using VirtualEmployee.Application.ScheduleBlocks.CreateScheduleBlock;
using VirtualEmployee.Infrastructure.ScheduleBlocks;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Scheduling;

[Collection(PostgreSqlCollection.Name)]
public sealed class ScheduleBlockPersistenceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    public ScheduleBlockPersistenceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    #region Facts

    [Fact]
    public async Task ScheduleBlock_WithMissingProfessionalLocation_ShouldRejectReference()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var unlinkedProfessionalId = Guid.NewGuid();
        var blockId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(tenantId))
            {
                context.Professionals.Add(
                    new Professional(
                        unlinkedProfessionalId,
                        tenantId,
                        businessId,
                        "Unlinked Professional",
                        CreatedAt));

                await context.SaveChangesAsync();

                context.ScheduleBlocks.Add(
                    new ScheduleBlock(
                        blockId,
                        tenantId,
                        locationId,
                        unlinkedProfessionalId,
                        CreatedAt.AddDays(1),
                        CreatedAt.AddDays(1).AddHours(1),
                        CreatedAt));

                var exception =
                    await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync());

                var postgresException =
                    Assert.IsType<Npgsql.PostgresException>(
                        exception.InnerException);

                Assert.Equal(
                    Npgsql.PostgresErrorCodes.ForeignKeyViolation,
                    postgresException.SqlState);

                Assert.Equal(
                    "schedule_blocks",
                    postgresException.TableName);

                Assert.Equal(
                    "FK_schedule_blocks_professional_locations_tenant_id_profession~",
                    postgresException.ConstraintName);
            }

            await using var readContext = CreateContext(tenantId);

            Assert.False(
                await readContext.ScheduleBlocks
                    .AnyAsync(x => x.Id == blockId));
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ScheduleBlock_ShouldPersistDeactivationAndPreserveAuditDates()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var blockId = Guid.NewGuid();
        var startsAt = CreatedAt.AddDays(1);
        var endsAt = startsAt.AddHours(1);
        var deactivatedAt = CreatedAt.AddHours(2);

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(tenantId))
            {
                context.ScheduleBlocks.Add(
                    new ScheduleBlock(
                        blockId,
                        tenantId,
                        locationId,
                        professionalId,
                        startsAt,
                        endsAt,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(tenantId))
            {
                var block = await context.ScheduleBlocks
                    .SingleAsync(x => x.Id == blockId);

                block.Deactivate(deactivatedAt);

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(tenantId))
            {
                var block = await context.ScheduleBlocks
                    .SingleAsync(x => x.Id == blockId);

                Assert.False(block.IsActive);
                Assert.Equal(CreatedAt, block.CreatedAt);
                Assert.Equal(deactivatedAt, block.UpdatedAt);

                block.Deactivate(CreatedAt.AddHours(3));

                await context.SaveChangesAsync();
            }

            await using var readContext = CreateContext(tenantId);

            var persistedBlock = await readContext.ScheduleBlocks
                .AsNoTracking()
                .SingleAsync(x => x.Id == blockId);

            Assert.Equal(tenantId, persistedBlock.TenantId);
            Assert.Equal(locationId, persistedBlock.LocationId);
            Assert.Equal(professionalId, persistedBlock.ProfessionalId);
            Assert.Equal(startsAt, persistedBlock.StartsAt);
            Assert.Equal(endsAt, persistedBlock.EndsAt);
            Assert.False(persistedBlock.IsActive);
            Assert.Equal(CreatedAt, persistedBlock.CreatedAt);
            Assert.Equal(deactivatedAt, persistedBlock.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ScheduleBlockCreation_AfterDeactivation_ShouldReplayOriginalResponse()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            var command = new CreateScheduleBlockCommand(
                locationId,
                professionalId,
                CreatedAt.AddDays(1),
                CreatedAt.AddDays(1).AddHours(1),
                Guid.NewGuid().ToString("N"));

            CreateScheduleBlockResult created;

            await using (var context = CreateContext(tenantId))
            {
                var handler = new CreateScheduleBlockHandler(
                    new ScheduleBlockCreationService(context));

                created = await handler.HandleAsync(command);
            }

            Assert.True(created.IsSuccess);
            Assert.NotNull(created.Response);

            var deactivatedAt = created.Response.CreatedAt.AddHours(1);

            await using (var context = CreateContext(tenantId))
            {
                var block = await context.ScheduleBlocks
                    .SingleAsync(x => x.Id == created.Response.Id);

                block.Deactivate(deactivatedAt);

                var location = await context.Locations
                    .SingleAsync(x => x.Id == locationId);

                location.Update(
                    location.Name,
                    location.CountryCode,
                    location.Timezone,
                    false,
                    deactivatedAt);

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(tenantId))
            {
                var handler = new CreateScheduleBlockHandler(
                    new ScheduleBlockCreationService(context));

                var replay = await handler.HandleAsync(command);

                Assert.True(replay.IsSuccess);
                Assert.True(replay.IsReplay);
                Assert.Equal(created.Response, replay.Response);
                Assert.NotNull(replay.Response);
                Assert.True(replay.Response.IsActive);
            }

            await using var readContext = CreateContext(tenantId);

            var persistedBlock = await readContext.ScheduleBlocks
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(created.Response.Id, persistedBlock.Id);
            Assert.False(persistedBlock.IsActive);
            Assert.Equal(created.Response.CreatedAt, persistedBlock.CreatedAt);
            Assert.Equal(deactivatedAt, persistedBlock.UpdatedAt);

            var persistedLocation = await readContext.Locations
                .AsNoTracking()
                .SingleAsync(x => x.Id == locationId);

            Assert.False(persistedLocation.IsActive);

            Assert.Equal(
                1,
                await readContext.IdempotencyRecords.CountAsync());
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Fact]
    public async Task ScheduleBlockCreation_WithSameKeyAcrossTenants_ShouldRemainIsolated()
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var businessAId = Guid.NewGuid();
        var businessBId = Guid.NewGuid();
        var locationAId = Guid.NewGuid();
        var locationBId = Guid.NewGuid();
        var professionalAId = Guid.NewGuid();
        var professionalBId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString("N");

        try
        {
            await SeedAsync(
                tenantAId, businessAId, locationAId, professionalAId);

            await SeedAsync(
                tenantBId, businessBId, locationBId, professionalBId);

            var commandA = new CreateScheduleBlockCommand(
                locationAId,
                professionalAId,
                CreatedAt.AddDays(1),
                CreatedAt.AddDays(1).AddHours(1),
                key);

            var commandB = commandA with
            {
                LocationId = locationBId,
                ProfessionalId = professionalBId
            };

            CreateScheduleBlockResult resultA;
            CreateScheduleBlockResult resultB;

            await using (var context = CreateContext(tenantAId))
            {
                var handler = new CreateScheduleBlockHandler(
                    new ScheduleBlockCreationService(context));

                resultA = await handler.HandleAsync(commandA);
            }

            await using (var context = CreateContext(tenantBId))
            {
                var handler = new CreateScheduleBlockHandler(
                    new ScheduleBlockCreationService(context));

                resultB = await handler.HandleAsync(commandB);
            }

            Assert.True(resultA.IsSuccess);
            Assert.True(resultB.IsSuccess);
            Assert.False(resultA.IsReplay);
            Assert.False(resultB.IsReplay);
            Assert.NotNull(resultA.Response);
            Assert.NotNull(resultB.Response);
            Assert.NotEqual(resultA.Response.Id, resultB.Response.Id);

            await using (var contextA = CreateContext(tenantAId))
            {
                var blockA = await contextA.ScheduleBlocks
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal(resultA.Response.Id, blockA.Id);
                Assert.Equal(tenantAId, blockA.TenantId);

                var recordA = await contextA.IdempotencyRecords
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal(tenantAId, recordA.TenantId);
                Assert.Equal(key, recordA.Key);

                Assert.False(
                    await contextA.ScheduleBlocks
                        .AnyAsync(x => x.Id == resultB.Response.Id));
            }

            await using (var contextB = CreateContext(tenantBId))
            {
                var blockB = await contextB.ScheduleBlocks
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal(resultB.Response.Id, blockB.Id);
                Assert.Equal(tenantBId, blockB.TenantId);

                var recordB = await contextB.IdempotencyRecords
                    .AsNoTracking()
                    .SingleAsync();

                Assert.Equal(tenantBId, recordB.TenantId);
                Assert.Equal(key, recordB.Key);

                var handler = new CreateScheduleBlockHandler(
                    new ScheduleBlockCreationService(contextB));

                var replayB = await handler.HandleAsync(commandB);

                Assert.True(replayB.IsSuccess);
                Assert.True(replayB.IsReplay);
                Assert.Equal(resultB.Response, replayB.Response);
            }
        }
        finally
        {
            await CleanupAsync(tenantAId);
            await CleanupAsync(tenantBId);
        }
    }

    [Fact]
    public async Task ScheduleBlockRead_ShouldFilterPeriodScopeAndActiveState()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var secondProfessionalId = Guid.NewGuid();

        var locationBlockId = Guid.NewGuid();
        var professionalBlockId = Guid.NewGuid();
        var secondProfessionalBlockId = Guid.NewGuid();

        var from = CreatedAt.AddDays(1);
        var to = from.AddHours(2);

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(tenantId))
            {
                context.Professionals.Add(
                    new Professional(
                        secondProfessionalId,
                        tenantId,
                        businessId,
                        "Second Professional",
                        CreatedAt));

                await context.SaveChangesAsync();

                context.ProfessionalLocations.Add(
                    new ProfessionalLocation(
                        tenantId,
                        secondProfessionalId,
                        locationId,
                        CreatedAt));

                await context.SaveChangesAsync();

                var inactiveBlock = new ScheduleBlock(
                    Guid.NewGuid(), tenantId, locationId, null,
                    from, to, CreatedAt);

                inactiveBlock.Deactivate(CreatedAt.AddHours(1));

                context.ScheduleBlocks.AddRange(
                    new ScheduleBlock(
                        locationBlockId, tenantId, locationId, null,
                        from.AddMinutes(-30), from.AddMinutes(30),
                        CreatedAt),
                    new ScheduleBlock(
                        professionalBlockId, tenantId, locationId,
                        professionalId,
                        from.AddMinutes(30), from.AddMinutes(60),
                        CreatedAt),
                    new ScheduleBlock(
                        secondProfessionalBlockId, tenantId, locationId,
                        secondProfessionalId,
                        from.AddMinutes(60), from.AddMinutes(90),
                        CreatedAt),
                    new ScheduleBlock(
                        Guid.NewGuid(), tenantId, locationId, null,
                        from.AddHours(-1), from,
                        CreatedAt),
                    new ScheduleBlock(
                        Guid.NewGuid(), tenantId, locationId, null,
                        to, to.AddHours(1),
                        CreatedAt),
                    inactiveBlock);

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(tenantId))
            {
                var readService = new ScheduleBlockReadService(context);

                Assert.True(
                    await readService.LocationExistsAsync(locationId));

                var allBlocks = await readService.GetByPeriodAsync(
                    locationId, from, to, professionalId: null);

                Assert.Equal(
                    new[]
                    {
                        locationBlockId,
                        professionalBlockId,
                        secondProfessionalBlockId
                    },
                    allBlocks.Select(block => block.Id).ToArray());

                var selectedBlocks = await readService.GetByPeriodAsync(
                    locationId,
                    from.ToOffset(TimeSpan.FromHours(-3)),
                    to.ToOffset(TimeSpan.FromHours(2)),
                    professionalId);

                Assert.Equal(
                    new[] { locationBlockId, professionalBlockId },
                    selectedBlocks.Select(block => block.Id).ToArray());

                var secondBlocks = await readService.GetByPeriodAsync(
                    locationId, from, to, secondProfessionalId);

                Assert.Equal(
                    new[] { locationBlockId, secondProfessionalBlockId },
                    secondBlocks.Select(block => block.Id).ToArray());

                Assert.All(allBlocks, block => Assert.True(block.IsActive));
            }

            await using var otherContext = CreateContext(Guid.NewGuid());
            var otherReadService = new ScheduleBlockReadService(otherContext);

            Assert.False(
                await otherReadService.LocationExistsAsync(locationId));

            Assert.Empty(
                await otherReadService.GetByPeriodAsync(
                    locationId, from, to, professionalId: null));
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduleBlockCreation_ShouldPersistOnceAndReplayAcrossOffsets(
        bool professionalScoped)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString("N");

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            var command = new CreateScheduleBlockCommand(
                locationId,
                professionalScoped ? professionalId : null,
                CreatedAt.AddDays(1),
                CreatedAt.AddDays(1).AddHours(1),
                key);

            CreateScheduleBlockResult created;

            await using (var context = CreateContext(tenantId))
            {
                var handler = new CreateScheduleBlockHandler(
                    new ScheduleBlockCreationService(context));

                created = await handler.HandleAsync(command);
            }

            Assert.True(created.IsSuccess);
            Assert.False(created.IsReplay);
            Assert.NotNull(created.Response);
            Assert.NotEqual(Guid.Empty, created.Response.Id);
            Assert.Equal(locationId, created.Response.LocationId);
            Assert.Equal(command.ProfessionalId, created.Response.ProfessionalId);
            Assert.Equal(command.StartsAt, created.Response.StartsAt);
            Assert.Equal(command.EndsAt, created.Response.EndsAt);

            // Mesmos instantes com offsets diferentes e um contexto novo.
            var replayCommand = command with
            {
                StartsAt = command.StartsAt.ToOffset(TimeSpan.FromHours(-3)),
                EndsAt = command.EndsAt.ToOffset(TimeSpan.FromHours(2))
            };

            await using (var context = CreateContext(tenantId))
            {
                var handler = new CreateScheduleBlockHandler(
                    new ScheduleBlockCreationService(context));

                var replay = await handler.HandleAsync(replayCommand);

                Assert.True(replay.IsSuccess);
                Assert.True(replay.IsReplay);
                Assert.Equal(created.Response, replay.Response);
            }

            await using var readContext = CreateContext(tenantId);

            var block = await readContext.ScheduleBlocks
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(created.Response.Id, block.Id);
            Assert.True(block.IsActive);
            Assert.Equal(created.Response.CreatedAt, block.CreatedAt);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(tenantId, record.TenantId);
            Assert.Equal("CreateScheduleBlock", record.Operation);
            Assert.Equal(key, record.Key);
            Assert.Equal(
                ScheduleBlockRequestHasher.Compute(command),
                record.RequestHash);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduleBlock_ShouldPersistScopeAndUtcInstants(
        bool professionalScoped)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var blockId = Guid.NewGuid();

        var startsAt = new DateTimeOffset(
            2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(-3));

        var endsAt = startsAt.AddHours(1);

        Guid? selectedProfessionalId =
            professionalScoped ? professionalId : null;

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(tenantId))
            {
                context.ScheduleBlocks.Add(
                    new ScheduleBlock(
                        blockId,
                        tenantId,
                        locationId,
                        selectedProfessionalId,
                        startsAt,
                        endsAt,
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using var readContext = CreateContext(tenantId);

            var block = await readContext.ScheduleBlocks
                .AsNoTracking()
                .SingleAsync(x => x.Id == blockId);

            Assert.Equal(blockId, block.Id);
            Assert.Equal(tenantId, block.TenantId);
            Assert.Equal(locationId, block.LocationId);
            Assert.Equal(selectedProfessionalId, block.ProfessionalId);
            Assert.True(block.IsActive);
            Assert.Equal(startsAt.ToUniversalTime(), block.StartsAt);
            Assert.Equal(endsAt.ToUniversalTime(), block.EndsAt);
            Assert.Equal(TimeSpan.Zero, block.StartsAt.Offset);
            Assert.Equal(TimeSpan.Zero, block.EndsAt.Offset);
            Assert.Equal(CreatedAt, block.CreatedAt);
            Assert.Equal(CreatedAt, block.UpdatedAt);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduleBlock_ShouldBeInvisibleToAnotherTenant(
        bool professionalScoped)
    {
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var blockId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                ownerTenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(ownerTenantId))
            {
                context.ScheduleBlocks.Add(
                    new ScheduleBlock(
                        blockId,
                        ownerTenantId,
                        locationId,
                        professionalScoped ? professionalId : null,
                        CreatedAt.AddDays(1),
                        CreatedAt.AddDays(1).AddHours(1),
                        CreatedAt));

                await context.SaveChangesAsync();
            }

            await using (var otherContext = CreateContext(otherTenantId))
            {
                Assert.False(
                    await otherContext.ScheduleBlocks
                        .AnyAsync(x => x.Id == blockId));

                // Confirma que o registro existe fisicamente.
                var physicalBlock = await otherContext.ScheduleBlocks
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == blockId);

                Assert.Equal(ownerTenantId, physicalBlock.TenantId);
            }

            await using var ownerContext = CreateContext(ownerTenantId);

            var ownerBlock = await ownerContext.ScheduleBlocks
                .AsNoTracking()
                .SingleAsync(x => x.Id == blockId);

            Assert.Equal(ownerTenantId, ownerBlock.TenantId);
            Assert.True(ownerBlock.IsActive);
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
    public async Task ScheduleBlock_ShouldRejectCrossTenantWrites(
        string operation)
    {
        var ownerTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var blockId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                ownerTenantId,
                businessId,
                locationId,
                professionalId);

            if (operation != "insert")
            {
                await using var ownerContext = CreateContext(ownerTenantId);

                ownerContext.ScheduleBlocks.Add(
                    new ScheduleBlock(
                        blockId,
                        ownerTenantId,
                        locationId,
                        professionalId,
                        CreatedAt.AddDays(1),
                        CreatedAt.AddDays(1).AddHours(1),
                        CreatedAt));

                await ownerContext.SaveChangesAsync();
            }

            await using (var otherContext = CreateContext(otherTenantId))
            {
                if (operation == "insert")
                {
                    otherContext.ScheduleBlocks.Add(
                        new ScheduleBlock(
                            blockId,
                            ownerTenantId,
                            locationId,
                            professionalId,
                            CreatedAt.AddDays(1),
                            CreatedAt.AddDays(1).AddHours(1),
                            CreatedAt));
                }
                else
                {
                    var block = await otherContext.ScheduleBlocks
                        .IgnoreQueryFilters()
                        .SingleAsync(x => x.Id == blockId);

                    if (operation == "update")
                    {
                        block.Deactivate(CreatedAt.AddHours(1));
                    }
                    else
                    {
                        otherContext.ScheduleBlocks.Remove(block);
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

            var persistedBlock = await verificationContext.ScheduleBlocks
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == blockId);

            if (operation == "insert")
            {
                Assert.Null(persistedBlock);
            }
            else
            {
                Assert.NotNull(persistedBlock);
                Assert.Equal(ownerTenantId, persistedBlock.TenantId);
                Assert.True(persistedBlock.IsActive);
                Assert.Equal(CreatedAt, persistedBlock.CreatedAt);
                Assert.Equal(CreatedAt, persistedBlock.UpdatedAt);
            }
        }
        finally
        {
            await CleanupAsync(ownerTenantId);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ScheduleBlock_WithInvalidInterval_ShouldBeRejectedByDatabase(
        int durationHours)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var blockId = Guid.NewGuid();
        var startsAt = CreatedAt.AddDays(1);

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            await using (var context = CreateContext(tenantId))
            {
                var block = new ScheduleBlock(
                    blockId,
                    tenantId,
                    locationId,
                    null,
                    startsAt,
                    startsAt.AddHours(1),
                    CreatedAt);

                var entry = context.ScheduleBlocks.Add(block);

                // Contorna o construtor somente para testar a constraint.
                entry.Property(x => x.EndsAt).CurrentValue =
                    startsAt.AddHours(durationHours);

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
                    "schedule_blocks",
                    postgresException.TableName);

                Assert.Equal(
                    "CK_schedule_blocks_time_window",
                    postgresException.ConstraintName);
            }

            await using var readContext = CreateContext(tenantId);

            Assert.False(
                await readContext.ScheduleBlocks
                    .AnyAsync(x => x.Id == blockId));
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("professional")]
    [InlineData("location")]
    public async Task ScheduleBlockCreation_WithSameKeyAndDifferentPayload_ShouldReturnConflict(
        string changedField)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString("N");

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            var command = new CreateScheduleBlockCommand(
                locationId,
                professionalId,
                CreatedAt.AddDays(1),
                CreatedAt.AddDays(1).AddHours(1),
                key);

            CreateScheduleBlockResult created;

            await using (var context = CreateContext(tenantId))
            {
                var handler = new CreateScheduleBlockHandler(
                    new ScheduleBlockCreationService(context));

                created = await handler.HandleAsync(command);
            }

            Assert.True(created.IsSuccess);
            Assert.NotNull(created.Response);

            var changedCommand = changedField switch
            {
                "start" => command with
                {
                    StartsAt = command.StartsAt.AddMinutes(10)
                },
                "end" => command with
                {
                    EndsAt = command.EndsAt.AddMinutes(10)
                },
                "professional" => command with
                {
                    ProfessionalId = null
                },
                "location" => command with
                {
                    LocationId = Guid.NewGuid()
                },
                _ => throw new ArgumentOutOfRangeException(nameof(changedField))
            };

            await using (var context = CreateContext(tenantId))
            {
                var handler = new CreateScheduleBlockHandler(
                    new ScheduleBlockCreationService(context));

                var conflict = await handler.HandleAsync(changedCommand);

                Assert.False(conflict.IsSuccess);
                Assert.Equal(
                    CreateScheduleBlockError.IdempotencyConflict,
                    conflict.Error);
                Assert.Null(conflict.Response);
                Assert.False(conflict.IsReplay);

                // O conteúdo original continua disponível para replay.
                var replay = await handler.HandleAsync(command);

                Assert.True(replay.IsSuccess);
                Assert.True(replay.IsReplay);
                Assert.Equal(created.Response, replay.Response);
            }

            await using var readContext = CreateContext(tenantId);

            var block = await readContext.ScheduleBlocks
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(created.Response.Id, block.Id);
            Assert.Equal(command.LocationId, block.LocationId);
            Assert.Equal(command.ProfessionalId, block.ProfessionalId);
            Assert.Equal(command.StartsAt, block.StartsAt);
            Assert.Equal(command.EndsAt, block.EndsAt);
            Assert.True(block.IsActive);
            Assert.Equal(created.Response.CreatedAt, block.CreatedAt);
            Assert.Equal(created.Response.UpdatedAt, block.UpdatedAt);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(key, record.Key);
            Assert.Equal(
                ScheduleBlockRequestHasher.Compute(command),
                record.RequestHash);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduleBlockCreation_WithConcurrentRequests_ShouldPersistOnlyWinner(
        bool differentPayload)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();

        try
        {
            await SeedAsync(
                tenantId,
                businessId,
                locationId,
                professionalId);

            var firstCommand = new CreateScheduleBlockCommand(
                locationId,
                professionalId,
                CreatedAt.AddDays(1),
                CreatedAt.AddDays(1).AddHours(1),
                Guid.NewGuid().ToString("N"));

            var secondCommand = differentPayload
                ? firstCommand with
                {
                    EndsAt = firstCommand.EndsAt.AddMinutes(30)
                }
                : firstCommand;

            var gate = new ConcurrentSaveGate();

            async Task<CreateScheduleBlockResult> CreateAttemptAsync(
                CreateScheduleBlockCommand command)
            {
                await using var context = CreateContext(tenantId, gate);

                var handler = new CreateScheduleBlockHandler(
                    new ScheduleBlockCreationService(context));

                return await handler.HandleAsync(command);
            }

            var results = await Task.WhenAll(
                CreateAttemptAsync(firstCommand),
                CreateAttemptAsync(secondCommand));

            Assert.Equal(2, gate.Arrivals);

            var winner = Assert.Single(
                results,
                result => result.IsSuccess && !result.IsReplay);

            Assert.NotNull(winner.Response);

            if (differentPayload)
            {
                var loser = Assert.Single(
                    results,
                    result => !result.IsSuccess);

                Assert.Equal(
                    CreateScheduleBlockError.IdempotencyConflict,
                    loser.Error);
                Assert.Null(loser.Response);
            }
            else
            {
                var replay = Assert.Single(
                    results,
                    result => result.IsReplay);

                Assert.True(replay.IsSuccess);
                Assert.Equal(winner.Response, replay.Response);
            }

            await using var readContext = CreateContext(tenantId);

            var block = await readContext.ScheduleBlocks
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(winner.Response.Id, block.Id);
            Assert.Equal(winner.Response.StartsAt, block.StartsAt);
            Assert.Equal(winner.Response.EndsAt, block.EndsAt);

            var record = await readContext.IdempotencyRecords
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(firstCommand.IdempotencyKey, record.Key);

            var winnerCommand = winner.Response.EndsAt == firstCommand.EndsAt
                ? firstCommand
                : secondCommand;

            Assert.Equal(
                ScheduleBlockRequestHasher.Compute(winnerCommand),
                record.RequestHash);
        }
        finally
        {
            await CleanupAsync(tenantId);
        }
    }

    #endregion

    #region Helpers

    private sealed class ConcurrentSaveGate
        : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private readonly TaskCompletionSource<bool> _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _arrivals;

        public int Arrivals => Volatile.Read(ref _arrivals);

        public override async ValueTask<
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>>
            SavingChangesAsync(
                Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
            {
                _ready.TrySetResult(true);
            }

            await _ready.Task.WaitAsync(
                TimeSpan.FromSeconds(30),
                cancellationToken);

            return result;
        }
    }

    private AppDbContext CreateContext(
        Guid tenantId,
        Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor?
            interceptor = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fixture.ConnectionString);

        if (interceptor is not null)
        {
            optionsBuilder.AddInterceptors(interceptor);
        }

        var tenantContext = new TenantContext();
        tenantContext.Initialize(tenantId);

        return new AppDbContext(optionsBuilder.Options, tenantContext);
    }

    private async Task SeedAsync(
        Guid tenantId,
        Guid businessId,
        Guid locationId,
        Guid professionalId)
    {
        await using var context = CreateContext(tenantId);

        context.Tenants.Add(
            new Tenant(tenantId, "Schedule Block Tenant"));

        context.Businesses.Add(
            new Business(
                businessId,
                tenantId,
                BusinessTypeIds.Barbershop,
                "Schedule Block Business",
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

        context.ScheduleBlocks.RemoveRange(
            await context.ScheduleBlocks.ToListAsync());
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

        context.IdempotencyRecords.RemoveRange(
            await context.IdempotencyRecords.ToListAsync());

        await context.SaveChangesAsync();
        var tenant = await context.Tenants
            .SingleOrDefaultAsync(x => x.Id == tenantId);

        if (tenant is not null)
        {
            context.Tenants.Remove(tenant);
            await context.SaveChangesAsync();
        }
    }

    #endregion
}