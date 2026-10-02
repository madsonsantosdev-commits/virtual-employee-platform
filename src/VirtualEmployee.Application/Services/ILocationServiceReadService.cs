using VirtualEmployee.Application.Services;

namespace VirtualEmployee.Application.LocationServices;

public interface ILocationServiceReadService
{
    Task<IReadOnlyList<ServiceResponse>> GetByLocationIdAsync(
        Guid locationId,
        CancellationToken cancellationToken = default);
}