using VirtualEmployee.Application.Appointments.CancelAppointment;

namespace VirtualEmployee.Application.Appointments;

public interface IAppointmentCancellationService
{
    Task<CancelAppointmentResult> CancelAsync(
        CancelAppointmentCommand command,
        CancellationToken cancellationToken = default);
}