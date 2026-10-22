namespace VirtualEmployee.Application.Appointments.GetAppointmentHistory;

public sealed record GetAppointmentHistoryResult(
    GetAppointmentHistoryError Error,
    GetAppointmentHistoryResponse? Response)
{
    public bool IsSuccess => Error == GetAppointmentHistoryError.None;

    public static GetAppointmentHistoryResult Success(
        GetAppointmentHistoryResponse response)
    {
        return new GetAppointmentHistoryResult(
            GetAppointmentHistoryError.None,
            response);
    }

    public static GetAppointmentHistoryResult Failure(
        GetAppointmentHistoryError error)
    {
        return new GetAppointmentHistoryResult(error, null);
    }
}