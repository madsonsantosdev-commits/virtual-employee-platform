using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Domain.Locations;

namespace VirtualEmployee.Application.Locations.CreateLocation;

public sealed class CreateLocationHandler
{
    private readonly ITenantContext _tenantContext;
    private readonly ILocationWriteService _locationWriteService;

    public CreateLocationHandler(
        ITenantContext tenantContext,
        ILocationWriteService locationWriteService)
    {
        _tenantContext = tenantContext;
        _locationWriteService = locationWriteService;
    }

    public async Task<LocationResponse?> HandleAsync(
        CreateLocationCommand command,
        CancellationToken cancellationToken = default)
    {
        var businessExists =
            await _locationWriteService.BusinessExistsAsync(
                command.BusinessId,
                cancellationToken);

        if (!businessExists)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;

        var location = new Location(
            Guid.NewGuid(),
            _tenantContext.TenantId,
            command.BusinessId,
            command.Name,
            command.CountryCode,
            command.Timezone,
            now,
            command.Phone,
            command.Address);

        await _locationWriteService.AddAsync(
            location,
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