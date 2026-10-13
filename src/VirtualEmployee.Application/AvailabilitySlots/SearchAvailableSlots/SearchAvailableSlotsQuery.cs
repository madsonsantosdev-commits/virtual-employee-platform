namespace VirtualEmployee.Application.AvailabilitySlots.SearchAvailableSlots;

public sealed record SearchAvailableSlotsQuery(
    Guid LocationId,
    IReadOnlyList<Guid> ServiceIds,
    DateOnly Date,
    Guid? ProfessionalId = null);