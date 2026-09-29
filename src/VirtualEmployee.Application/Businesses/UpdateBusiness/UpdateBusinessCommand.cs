namespace VirtualEmployee.Application.Businesses.UpdateBusiness;

public sealed record UpdateBusinessCommand(
    Guid BusinessTypeId,
    string Name,
    int SlotIntervalMinutes,
    bool IsActive);