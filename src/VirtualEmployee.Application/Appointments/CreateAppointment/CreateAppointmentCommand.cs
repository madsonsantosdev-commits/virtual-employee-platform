namespace VirtualEmployee.Application.Appointments.CreateAppointment;

public sealed record CreateAppointmentCommand(
    Guid CustomerId,
    Guid LocationId,
    Guid ProfessionalId,
    IReadOnlyList<Guid> ServiceIds,
    DateTimeOffset StartsAt,
    string IdempotencyKey);