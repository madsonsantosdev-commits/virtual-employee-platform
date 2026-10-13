namespace VirtualEmployee.Application.AvailabilitySlots;

public sealed record AvailabilityWindow(
    Guid ProfessionalId,
    TimeOnly StartTime,
    TimeOnly EndTime);