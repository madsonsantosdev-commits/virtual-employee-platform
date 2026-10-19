namespace VirtualEmployee.Application.Appointments.GetAppointments;

public sealed record GetAppointmentsResponse(
    IReadOnlyList<AppointmentResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);