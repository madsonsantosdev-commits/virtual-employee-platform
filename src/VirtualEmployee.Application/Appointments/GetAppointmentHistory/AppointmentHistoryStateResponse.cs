namespace VirtualEmployee.Application.Appointments.GetAppointmentHistory;

public sealed record AppointmentHistoryStateResponse(
    string Status,
    Guid LocationId,
    Guid ProfessionalId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset? ReservationExpiresAt);