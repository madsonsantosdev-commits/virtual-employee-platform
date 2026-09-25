namespace VirtualEmployee.Application.Locations.CreateLocation;

public sealed record CreateLocationCommand(
    Guid BusinessId,
    string Name,
    string CountryCode,
    string Timezone,
    string? Phone,
    string? Address);