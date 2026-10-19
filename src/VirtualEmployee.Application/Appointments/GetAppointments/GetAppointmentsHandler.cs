namespace VirtualEmployee.Application.Appointments.GetAppointments;

public sealed class GetAppointmentsHandler
{
    private readonly IAppointmentReadService _readService;

    public GetAppointmentsHandler(IAppointmentReadService readService)
    {
        _readService = readService;
    }

    public async Task<GetAppointmentsResult> HandleAsync(
        GetAppointmentsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.LocationId == Guid.Empty ||
            query.ProfessionalId == Guid.Empty ||
            query.CustomerId == Guid.Empty)
        {
            return GetAppointmentsResult.Failure(
                GetAppointmentsError.InvalidRequest);
        }

        if (query.Status is { } status &&
            !Enum.IsDefined(status))
        {
            return GetAppointmentsResult.Failure(
                GetAppointmentsError.InvalidRequest);
        }

        if (query.Page < 1 ||
            query.PageSize < 1 ||
            query.PageSize > 100)
        {
            return GetAppointmentsResult.Failure(
                GetAppointmentsError.InvalidRequest);
        }

        var offset = ((long)query.Page - 1) * query.PageSize;

        if (offset > int.MaxValue)
        {
            return GetAppointmentsResult.Failure(
                GetAppointmentsError.InvalidRequest);
        }

        if (query.From.HasValue != query.To.HasValue)
        {
            return GetAppointmentsResult.Failure(
                GetAppointmentsError.InvalidRequest);
        }

        if (query.From is { } from &&
            query.To is { } to &&
            from >= to)
        {
            return GetAppointmentsResult.Failure(
                GetAppointmentsError.InvalidRequest);
        }

        var normalizedQuery = query with
        {
            From = query.From?.ToUniversalTime(),
            To = query.To?.ToUniversalTime()
        };

        var response = await _readService.GetAllAsync(
            normalizedQuery,
            cancellationToken);

        return GetAppointmentsResult.Success(response);
    }
}