using VirtualEmployee.Application.ProfessionalLocations;
using VirtualEmployee.Application.ProfessionalLocations.ReplaceProfessionalLocations;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Professionals;
using Xunit;

namespace VirtualEmployee.Application.Tests.ProfessionalLocations;

public sealed class ReplaceProfessionalLocationsHandlerTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_WithValidLocations_ShouldReplaceLocations()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professional = CreateProfessional(tenantId, businessId);
        var locationA = CreateLocation(tenantId, businessId);
        var locationB = CreateLocation(tenantId, businessId);

        var writeService = new FakeProfessionalLocationWriteService
        {
            Professional = professional,
            Locations = [locationA, locationB]
        };

        var handler = new ReplaceProfessionalLocationsHandler(writeService);
        var command = new ReplaceProfessionalLocationsCommand(
            professional.Id,
            [locationA.Id, locationB.Id]);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProfessionalLocationOperationError.None, result.Error);
        Assert.Equal(professional.Id, writeService.ReplacedProfessionalId);
        Assert.Equal(
            new[] { locationA.Id, locationB.Id },
            writeService.ReplacedLocationIds);
        Assert.NotNull(writeService.ReplacedAt);
        Assert.True(writeService.SaveChangesCalled);
    }

    [Fact]
    public async Task HandleAsync_WithEmptyLocationList_ShouldReplaceWithEmptyList()
    {
        var professional = CreateProfessional(
            Guid.NewGuid(),
            Guid.NewGuid());

        var writeService = new FakeProfessionalLocationWriteService
        {
            Professional = professional
        };

        var handler = new ReplaceProfessionalLocationsHandler(writeService);
        var command = new ReplaceProfessionalLocationsCommand(
            professional.Id,
            []);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal(professional.Id, writeService.ReplacedProfessionalId);
        Assert.NotNull(writeService.ReplacedLocationIds);
        Assert.Empty(writeService.ReplacedLocationIds);
        Assert.True(writeService.SaveChangesCalled);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownProfessional_ShouldReturnProfessionalNotFound()
    {
        var writeService = new FakeProfessionalLocationWriteService();
        var handler = new ReplaceProfessionalLocationsHandler(writeService);
        var command = new ReplaceProfessionalLocationsCommand(
            Guid.NewGuid(),
            [Guid.NewGuid()]);

        var result = await handler.HandleAsync(command);

        AssertRejected(
            result,
            ProfessionalLocationOperationError.ProfessionalNotFound,
            writeService);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownLocation_ShouldReturnLocationNotFound()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professional = CreateProfessional(tenantId, businessId);
        var existingLocation = CreateLocation(tenantId, businessId);

        var writeService = new FakeProfessionalLocationWriteService
        {
            Professional = professional,
            Locations = [existingLocation]
        };

        var handler = new ReplaceProfessionalLocationsHandler(writeService);
        var command = new ReplaceProfessionalLocationsCommand(
            professional.Id,
            [existingLocation.Id, Guid.NewGuid()]);

        var result = await handler.HandleAsync(command);

        AssertRejected(
            result,
            ProfessionalLocationOperationError.LocationNotFound,
            writeService);
    }

    [Fact]
    public async Task HandleAsync_WithLocationFromAnotherBusiness_ShouldReturnError()
    {
        var tenantId = Guid.NewGuid();
        var professional = CreateProfessional(tenantId, Guid.NewGuid());
        var location = CreateLocation(tenantId, Guid.NewGuid());

        var writeService = new FakeProfessionalLocationWriteService
        {
            Professional = professional,
            Locations = [location]
        };

        var handler = new ReplaceProfessionalLocationsHandler(writeService);
        var command = new ReplaceProfessionalLocationsCommand(
            professional.Id,
            [location.Id]);

        var result = await handler.HandleAsync(command);

        AssertRejected(
            result,
            ProfessionalLocationOperationError.LocationFromAnotherBusiness,
            writeService);
    }

    [Fact]
    public async Task HandleAsync_WithDuplicateLocationIds_ShouldReturnDuplicateLocation()
    {
        var professional = CreateProfessional(
            Guid.NewGuid(),
            Guid.NewGuid());

        var locationId = Guid.NewGuid();

        var writeService = new FakeProfessionalLocationWriteService
        {
            Professional = professional
        };

        var handler = new ReplaceProfessionalLocationsHandler(writeService);
        var command = new ReplaceProfessionalLocationsCommand(
            professional.Id,
            [locationId, locationId]);

        var result = await handler.HandleAsync(command);

        AssertRejected(
            result,
            ProfessionalLocationOperationError.DuplicateLocation,
            writeService);
    }

    private static void AssertRejected(
        ProfessionalLocationOperationResult result,
        ProfessionalLocationOperationError expectedError,
        FakeProfessionalLocationWriteService writeService)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        Assert.Null(writeService.ReplacedProfessionalId);
        Assert.Null(writeService.ReplacedLocationIds);
        Assert.False(writeService.SaveChangesCalled);
    }

    private static Professional CreateProfessional(
        Guid tenantId,
        Guid businessId)
    {
        return new Professional(
            Guid.NewGuid(),
            tenantId,
            businessId,
            "Professional",
            CreatedAt);
    }

    private static Location CreateLocation(
        Guid tenantId,
        Guid businessId)
    {
        return new Location(
            Guid.NewGuid(),
            tenantId,
            businessId,
            "Location",
            "BR",
            "America/Sao_Paulo",
            CreatedAt);
    }

    private sealed class FakeProfessionalLocationWriteService
        : IProfessionalLocationWriteService
    {
        public Professional? Professional { get; init; }

        public IReadOnlyList<Location> Locations { get; init; } = [];

        public Guid? ReplacedProfessionalId { get; private set; }

        public Guid[]? ReplacedLocationIds { get; private set; }

        public DateTimeOffset? ReplacedAt { get; private set; }

        public bool SaveChangesCalled { get; private set; }

        public Task<Professional?> GetProfessionalByIdAsync(
            Guid professionalId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Professional?.Id == professionalId
                    ? Professional
                    : null);
        }

        public Task<IReadOnlyList<Location>> GetLocationsByIdsAsync(
            IReadOnlyCollection<Guid> locationIds,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Location> locations = Locations
                .Where(location => locationIds.Contains(location.Id))
                .ToArray();

            return Task.FromResult(locations);
        }

        public Task ReplaceAsync(
            Guid professionalId,
            IReadOnlyCollection<Guid> locationIds,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            ReplacedProfessionalId = professionalId;
            ReplacedLocationIds = locationIds.ToArray();
            ReplacedAt = updatedAt;

            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;

            return Task.CompletedTask;
        }
    }
}