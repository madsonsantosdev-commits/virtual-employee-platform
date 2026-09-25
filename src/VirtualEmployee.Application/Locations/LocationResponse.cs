namespace VirtualEmployee.Application.Locations;

public sealed record LocationResponse(
    Guid Id,
    Guid BusinessId,
    string Name,
    string? Phone,
    string? Address,
    string CountryCode,
    string Timezone,
    bool IsActive);