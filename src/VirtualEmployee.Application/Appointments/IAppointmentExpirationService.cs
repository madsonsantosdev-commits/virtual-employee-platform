namespace VirtualEmployee.Application.Appointments;

public interface IAppointmentExpirationService
{
    Task<int> ExpireDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}