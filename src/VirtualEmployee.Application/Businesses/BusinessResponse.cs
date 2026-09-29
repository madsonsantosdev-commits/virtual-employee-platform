namespace VirtualEmployee.Application.Businesses;

public sealed record BusinessResponse(
    Guid Id,
    Guid BusinessTypeId,
    string Name,
    int SlotIntervalMinutes,
    bool IsActive);