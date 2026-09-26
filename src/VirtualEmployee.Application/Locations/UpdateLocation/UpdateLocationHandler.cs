namespace VirtualEmployee.Application.Locations.UpdateLocation;

public sealed class UpdateLocationHandler
{
    private readonly ILocationWriteService _locationWriteService;

    public UpdateLocationHandler(
        ILocationWriteService locationWriteService)
    {
        _locationWriteService = locationWriteService;
    }

    public async Task<LocationResponse?> HandleAsync(
        UpdateLocationCommand command,
        CancellationToken cancellationToken = default)
    {
        var location = await _locationWriteService.GetByIdAsync(
            command.LocationId,
            cancellationToken);

        if (location is null)
        {
            return null;
        }

        location.Update(
            command.Name,
            command.CountryCode,
            command.Timezone,
            command.IsActive,
            DateTimeOffset.UtcNow,
            command.Phone,
            command.Address);

        await _locationWriteService.SaveChangesAsync(
            cancellationToken);

        return new LocationResponse(
            location.Id,
            location.BusinessId,
            location.Name,
            location.Phone,
            location.Address,
            location.CountryCode,
            location.Timezone,
            location.IsActive);
    }
}