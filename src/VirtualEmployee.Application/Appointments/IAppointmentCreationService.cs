using VirtualEmployee.Application.Appointments.CreateAppointment;

namespace VirtualEmployee.Application.Appointments;

public interface IAppointmentCreationService
{
    Task<CreateAppointmentResult> CreateAsync(
        CreateAppointmentCommand command,
        CancellationToken cancellationToken = default);
}