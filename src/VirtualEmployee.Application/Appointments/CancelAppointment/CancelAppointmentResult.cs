namespace VirtualEmployee.Application.Appointments.CancelAppointment;

public sealed record CancelAppointmentResult(
    CancelAppointmentError Error,
    CancelAppointmentResponse? Response)
{
    public bool IsSuccess => Error == CancelAppointmentError.None;

    public static CancelAppointmentResult Success(
        CancelAppointmentResponse response)
    {
        return new(CancelAppointmentError.None, response);
    }

    public static CancelAppointmentResult Failure(
        CancelAppointmentError error)
    {
        return new(error, null);
    }
}