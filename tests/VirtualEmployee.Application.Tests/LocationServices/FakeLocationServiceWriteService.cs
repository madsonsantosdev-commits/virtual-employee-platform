using VirtualEmployee.Application.LocationServices;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Application.Tests.LocationServices;

internal sealed class FakeLocationServiceWriteService
    : ILocationServiceWriteService
{
    public Location? Location { get; set; }

    public IReadOnlyList<Service> Services { get; set; } = [];

    public Guid? ReplacedLocationId { get; private set; }

    public IReadOnlyCollection<Guid>? ReplacedServiceIds { get; private set; }

    public DateTimeOffset? ReplacedAt { get; private set; }

    public bool SaveChangesCalled { get; private set; }

    public Task<Location?> GetLocationByIdAsync(
        Guid locationId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            Location?.Id == locationId
                ? Location
                : null);
    }

    public Task<IReadOnlyList<Service>> GetServicesByIdsAsync(
        IReadOnlyCollection<Guid> serviceIds,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Service> result = Services
            .Where(service => serviceIds.Contains(service.Id))
            .ToArray();

        return Task.FromResult(result);
    }

    public Task ReplaceAsync(
        Guid locationId,
        IReadOnlyCollection<Guid> serviceIds,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ReplacedLocationId = locationId;
        ReplacedServiceIds = serviceIds.ToArray();
        ReplacedAt = createdAt;

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        SaveChangesCalled = true;

        return Task.CompletedTask;
    }
}