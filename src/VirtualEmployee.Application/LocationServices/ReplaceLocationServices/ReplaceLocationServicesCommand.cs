namespace VirtualEmployee.Application.LocationServices.ReplaceLocationServices;

public sealed record ReplaceLocationServicesCommand(
    Guid LocationId,
    IReadOnlyList<Guid> ServiceIds);