namespace VirtualEmployee.Application.Appointments.CreateAppointment;

public sealed record CreateAppointmentResult(
    CreateAppointmentError Error,
    AppointmentResponse? Response)
{
    public bool IsSuccess => Error == CreateAppointmentError.None;

    public static CreateAppointmentResult Success(
        AppointmentResponse response)
        => new(CreateAppointmentError.None, response);

    public static CreateAppointmentResult Failure(
        CreateAppointmentError error)
        => new(error, null);
}