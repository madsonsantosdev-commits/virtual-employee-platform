namespace VirtualEmployee.Application.ScheduleBlocks;

public interface IScheduleBlockRemovalService
{
    Task<bool> DeactivateAsync(
        Guid blockId,
        CancellationToken cancellationToken = default);
}