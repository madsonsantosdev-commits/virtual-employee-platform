namespace VirtualEmployee.Application.LocationServices.ReplaceLocationServices;

public sealed class ReplaceLocationServicesHandler
{
    private readonly ILocationServiceWriteService _writeService;

    public ReplaceLocationServicesHandler(
        ILocationServiceWriteService writeService)
    {
        _writeService = writeService;
    }

    public async Task<LocationServiceOperationResult> HandleAsync(
        ReplaceLocationServicesCommand command,
        CancellationToken cancellationToken = default)
    {
        var location = await _writeService.GetLocationByIdAsync(
            command.LocationId,
            cancellationToken);

        if (location is null)
        {
            return LocationServiceOperationResult.Failure(
                LocationServiceOperationError.LocationNotFound);
        }

        if (command.ServiceIds.Count !=
            command.ServiceIds.Distinct().Count())
        {
            return LocationServiceOperationResult.Failure(
                LocationServiceOperationError.DuplicateService);
        }

        var services = await _writeService.GetServicesByIdsAsync(
            command.ServiceIds,
            cancellationToken);

        if (services.Count != command.ServiceIds.Count)
        {
            return LocationServiceOperationResult.Failure(
                LocationServiceOperationError.ServiceNotFound);
        }

        if (services.Any(service =>
                service.BusinessId != location.BusinessId))
        {
            return LocationServiceOperationResult.Failure(
                LocationServiceOperationError.ServiceFromAnotherBusiness);
        }

        await _writeService.ReplaceAsync(
            command.LocationId,
            command.ServiceIds,
            DateTimeOffset.UtcNow,
            cancellationToken);

        await _writeService.SaveChangesAsync(
            cancellationToken);

        return LocationServiceOperationResult.Success();
    }
}