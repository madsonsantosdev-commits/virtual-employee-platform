namespace VirtualEmployee.Application.Appointments.CancelAppointment;

public sealed record CancelAppointmentResponse(
    Guid Id,
    string Status,
    string CancellationReason,
    DateTimeOffset CancelledAt,
    DateTimeOffset UpdatedAt);