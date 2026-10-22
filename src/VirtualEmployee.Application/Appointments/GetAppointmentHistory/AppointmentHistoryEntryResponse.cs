namespace VirtualEmployee.Application.Appointments.GetAppointmentHistory;

public sealed record AppointmentHistoryEntryResponse(
    Guid Id,
    Guid AppointmentId,
    string Type,
    DateTimeOffset OccurredAt,
    AppointmentHistoryStateResponse? Before,
    AppointmentHistoryStateResponse After,
    string? Reason);