using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Application.Services;

public sealed record ServiceResponse(
    Guid Id,
    Guid BusinessId,
    string Name,
    ServiceType ServiceType,
    decimal Price,
    int DurationMinutes,
    bool IsActive,
    IReadOnlyList<Guid> ComponentServiceIds);