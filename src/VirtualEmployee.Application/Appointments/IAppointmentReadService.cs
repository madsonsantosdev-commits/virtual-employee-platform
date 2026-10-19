using VirtualEmployee.Application.Appointments.GetAppointments;

namespace VirtualEmployee.Application.Appointments;

public interface IAppointmentReadService
{
    Task<AppointmentResponse?> GetByIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    Task<GetAppointmentsResponse> GetAllAsync(
        GetAppointmentsQuery query,
        CancellationToken cancellationToken = default);
}