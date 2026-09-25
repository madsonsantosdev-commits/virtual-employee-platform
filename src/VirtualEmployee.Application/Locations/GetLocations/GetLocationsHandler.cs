namespace VirtualEmployee.Application.Locations.GetLocations;

public sealed class GetLocationsHandler
{
    private readonly ILocationReadService _locationReadService;

    public GetLocationsHandler(
        ILocationReadService locationReadService)
    {
        _locationReadService = locationReadService;
    }

    public Task<IReadOnlyList<LocationResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        return _locationReadService.GetAllAsync(cancellationToken);
    }
}