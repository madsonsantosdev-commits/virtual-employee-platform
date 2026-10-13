namespace VirtualEmployee.Application.AvailabilitySlots.SearchAvailableSlots;

public sealed record SearchAvailableSlotsResponse(
    Guid LocationId,
    DateOnly Date,
    string Timezone,
    int TotalDurationMinutes,
    IReadOnlyList<AvailableSlotResponse> Slots);
