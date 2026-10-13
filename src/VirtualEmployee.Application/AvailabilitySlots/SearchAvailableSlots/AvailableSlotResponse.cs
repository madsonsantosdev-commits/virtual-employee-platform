namespace VirtualEmployee.Application.AvailabilitySlots.SearchAvailableSlots;

public sealed record AvailableSlotResponse(
    Guid ProfessionalId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);