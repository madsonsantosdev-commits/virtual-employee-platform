namespace VirtualEmployee.Application.Appointments.GetAppointmentHistory;

public sealed class GetAppointmentHistoryHandler
{
    private readonly IAppointmentHistoryReadService _readService;

    public GetAppointmentHistoryHandler(
        IAppointmentHistoryReadService readService)
    {
        _readService = readService;
    }

    public async Task<GetAppointmentHistoryResult> HandleAsync(
        GetAppointmentHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.AppointmentId == Guid.Empty ||
            query.Page < 1 ||
            query.PageSize < 1 ||
            query.PageSize > 100)
        {
            return GetAppointmentHistoryResult.Failure(
                GetAppointmentHistoryError.InvalidRequest);
        }

        var offset = ((long)query.Page - 1) * query.PageSize;

        if (offset > int.MaxValue)
        {
            return GetAppointmentHistoryResult.Failure(
                GetAppointmentHistoryError.InvalidRequest);
        }

        var response = await _readService.GetAsync(
            query,
            cancellationToken);

        return response is null
            ? GetAppointmentHistoryResult.Failure(
                GetAppointmentHistoryError.AppointmentNotFound)
            : GetAppointmentHistoryResult.Success(response);
    }
}