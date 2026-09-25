namespace VirtualEmployee.Application.Locations;

public interface ILocationReadService
{
    Task<IReadOnlyList<LocationResponse>> GetAllAsync(
        CancellationToken cancellationToken = default);
}