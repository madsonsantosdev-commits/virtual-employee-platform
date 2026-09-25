namespace VirtualEmployee.Application.Locations.GetLocations;

public sealed class GetLocationByIdHandler
{
    private readonly ILocationReadService _locationReadService;

    public GetLocationByIdHandler(
        ILocationReadService locationReadService)
    {
        _locationReadService = locationReadService;
    }

    public Task<LocationResponse?> HandleAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _locationReadService.GetByIdAsync(
            id,
            cancellationToken);
    }
}