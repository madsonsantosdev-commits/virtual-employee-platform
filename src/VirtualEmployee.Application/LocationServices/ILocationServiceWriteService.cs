using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Application.LocationServices;

public interface ILocationServiceWriteService
{
    Task<Location?> GetLocationByIdAsync(
        Guid locationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Service>> GetServicesByIdsAsync(
        IReadOnlyCollection<Guid> serviceIds,
        CancellationToken cancellationToken = default);

    Task ReplaceAsync(
        Guid locationId,
        IReadOnlyCollection<Guid> serviceIds,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}