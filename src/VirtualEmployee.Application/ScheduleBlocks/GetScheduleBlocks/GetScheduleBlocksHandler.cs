namespace VirtualEmployee.Application.ScheduleBlocks.GetScheduleBlocks;

public sealed class GetScheduleBlocksHandler
{
    private readonly IScheduleBlockReadService _readService;

    public GetScheduleBlocksHandler(
        IScheduleBlockReadService readService)
    {
        _readService = readService;
    }

    public async Task<IReadOnlyList<ScheduleBlockResponse>?> HandleAsync(
        GetScheduleBlocksQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.LocationId == Guid.Empty ||
            query.ProfessionalId == Guid.Empty ||
            query.From >= query.To)
        {
            throw new ArgumentException(
                "Location, professional and period must be valid.",
                nameof(query));
        }

        var exists = await _readService.LocationExistsAsync(
            query.LocationId,
            cancellationToken);

        if (!exists)
        {
            return null;
        }

        return await _readService.GetByPeriodAsync(
            query.LocationId,
            query.From.ToUniversalTime(),
            query.To.ToUniversalTime(),
            query.ProfessionalId,
            cancellationToken);
    }
}