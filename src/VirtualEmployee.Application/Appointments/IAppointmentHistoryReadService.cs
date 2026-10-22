using VirtualEmployee.Application.Appointments.GetAppointmentHistory;

namespace VirtualEmployee.Application.Appointments;

public interface IAppointmentHistoryReadService
{
    Task<GetAppointmentHistoryResponse?> GetAsync(
        GetAppointmentHistoryQuery query,
        CancellationToken cancellationToken = default);
}