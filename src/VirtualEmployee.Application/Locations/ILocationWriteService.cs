using VirtualEmployee.Domain.Locations;

namespace VirtualEmployee.Application.Locations;

public interface ILocationWriteService
{
    Task<bool> BusinessExistsAsync(
        Guid businessId,
        CancellationToken cancellationToken = default);

    Task<Location?> GetByIdAsync(
        Guid locationId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Location location,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}