using VirtualEmployee.Application.Services;

namespace VirtualEmployee.Application.LocationServices.GetLocationServices;

public sealed class GetLocationServicesHandler
{
    private readonly ILocationServiceReadService _locationServiceReadService;
    private readonly ILocationServiceWriteService _locationServiceWriteService;

    public GetLocationServicesHandler(
        ILocationServiceReadService locationServiceReadService,
        ILocationServiceWriteService locationServiceWriteService)
    {
        _locationServiceReadService = locationServiceReadService;
        _locationServiceWriteService = locationServiceWriteService;
    }

    public async Task<IReadOnlyList<ServiceResponse>?> HandleAsync(
        Guid locationId,
        CancellationToken cancellationToken = default)
    {
        var location =
            await _locationServiceWriteService.GetLocationByIdAsync(
                locationId,
                cancellationToken);

        if (location is null)
        {
            return null;
        }

        return await _locationServiceReadService.GetByLocationIdAsync(
            locationId,
            cancellationToken);
    }
}