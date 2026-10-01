using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Application.Services.CreateService;

public sealed record CreateServiceCommand(
    Guid BusinessId,
    string Name,
    ServiceType ServiceType,
    decimal Price,
    int DurationMinutes,
    IReadOnlyList<Guid>? ComponentServiceIds);