namespace VirtualEmployee.Application.Appointments;

public interface IAppointmentReadService
{
    Task<AppointmentResponse?> GetByIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);
}