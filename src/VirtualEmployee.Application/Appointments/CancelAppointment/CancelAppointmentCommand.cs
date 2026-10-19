using VirtualEmployee.Domain.Scheduling;

namespace VirtualEmployee.Application.Appointments.CancelAppointment;

public sealed record CancelAppointmentCommand(
    Guid AppointmentId,
    AppointmentStatus CancellationStatus,
    string Reason,
    string IdempotencyKey);