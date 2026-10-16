namespace VirtualEmployee.Application.Appointments.GetAppointmentById;

public sealed class GetAppointmentByIdHandler
{
    private readonly IAppointmentReadService _readService;

    public GetAppointmentByIdHandler(IAppointmentReadService readService)
    {
        _readService = readService;
    }

    public Task<AppointmentResponse?> HandleAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        if (appointmentId == Guid.Empty)
        {
            return Task.FromResult<AppointmentResponse?>(null);
        }

        return _readService.GetByIdAsync(
            appointmentId,
            cancellationToken);
    }
}