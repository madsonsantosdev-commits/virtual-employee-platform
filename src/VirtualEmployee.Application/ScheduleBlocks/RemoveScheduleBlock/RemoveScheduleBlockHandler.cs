namespace VirtualEmployee.Application.ScheduleBlocks.RemoveScheduleBlock;

public sealed class RemoveScheduleBlockHandler
{
    private readonly IScheduleBlockRemovalService _removalService;

    public RemoveScheduleBlockHandler(
        IScheduleBlockRemovalService removalService)
    {
        _removalService = removalService;
    }

    public Task<bool> HandleAsync(
        Guid blockId,
        CancellationToken cancellationToken = default)
    {
        if (blockId == Guid.Empty)
        {
            throw new ArgumentException(
                "Schedule block id cannot be empty.",
                nameof(blockId));
        }

        return _removalService.DeactivateAsync(
            blockId,
            cancellationToken);
    }
}