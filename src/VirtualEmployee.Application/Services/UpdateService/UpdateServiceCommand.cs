namespace VirtualEmployee.Application.Services.UpdateService;

public sealed record UpdateServiceCommand(
    Guid ServiceId,
    string Name,
    decimal Price,
    int DurationMinutes,
    bool IsActive,
    IReadOnlyList<Guid>? ComponentServiceIds);