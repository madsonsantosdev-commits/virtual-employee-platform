namespace VirtualEmployee.Application.Appointments.RescheduleAppointment;

public sealed record RescheduleAppointmentResult(
    RescheduleAppointmentError Error,
    AppointmentResponse? Response)
{
    public bool IsSuccess =>
        Error == RescheduleAppointmentError.None;

    public static RescheduleAppointmentResult Success(
        AppointmentResponse response)
    {
        return new(RescheduleAppointmentError.None, response);
    }

    public static RescheduleAppointmentResult Failure(
        RescheduleAppointmentError error)
    {
        return new(error, null);
    }
}