namespace VirtualEmployee.Application.Appointments.GetAppointmentHistory;

public sealed record GetAppointmentHistoryResponse(
    IReadOnlyList<AppointmentHistoryEntryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);