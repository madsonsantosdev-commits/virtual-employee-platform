namespace VirtualEmployee.Application.Appointments.GetAppointments;

public sealed record GetAppointmentsResult(
    GetAppointmentsError Error,
    GetAppointmentsResponse? Response)
{
    public bool IsSuccess => Error == GetAppointmentsError.None;

    public static GetAppointmentsResult Success(
        GetAppointmentsResponse response)
        => new(GetAppointmentsError.None, response);

    public static GetAppointmentsResult Failure(
        GetAppointmentsError error)
        => new(error, null);
}