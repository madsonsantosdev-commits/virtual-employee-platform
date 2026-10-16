namespace VirtualEmployee.Application.AvailabilitySlots;

public sealed record OccupiedAppointmentWindow(
    Guid ProfessionalId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);
