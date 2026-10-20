namespace VirtualEmployee.Application.Appointments.RescheduleAppointment;

public sealed record RescheduleAppointmentCommand(
    Guid AppointmentId,
    Guid LocationId,
    Guid ProfessionalId,
    DateTimeOffset StartsAt,
    string IdempotencyKey);