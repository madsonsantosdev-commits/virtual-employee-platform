namespace VirtualEmployee.Application.ScheduleBlocks;

public interface IScheduleBlockReadService
{
    Task<bool> LocationExistsAsync(
        Guid locationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScheduleBlockResponse>> GetByPeriodAsync(
        Guid locationId,
        DateTimeOffset from,
        DateTimeOffset to,
        Guid? professionalId,
        CancellationToken cancellationToken = default);
}