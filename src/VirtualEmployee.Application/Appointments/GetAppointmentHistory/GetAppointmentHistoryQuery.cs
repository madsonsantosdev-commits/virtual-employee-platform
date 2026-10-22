namespace VirtualEmployee.Application.Appointments.GetAppointmentHistory;

public sealed record GetAppointmentHistoryQuery(
    Guid AppointmentId,
    int Page = 1,
    int PageSize = 50);