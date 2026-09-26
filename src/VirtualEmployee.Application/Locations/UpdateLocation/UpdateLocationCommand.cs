namespace VirtualEmployee.Application.Locations.UpdateLocation;

public sealed record UpdateLocationCommand(
    Guid LocationId,
    string Name,
    string CountryCode,
    string Timezone,
    bool IsActive,
    string? Phone,
    string? Address);