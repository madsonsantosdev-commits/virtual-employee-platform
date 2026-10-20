using VirtualEmployee.Application.Appointments.RescheduleAppointment;

namespace VirtualEmployee.Application.Appointments;

public interface IAppointmentReschedulingService
{
    Task<RescheduleAppointmentResult> RescheduleAsync(
        RescheduleAppointmentCommand command,
        CancellationToken cancellationToken = default);
}