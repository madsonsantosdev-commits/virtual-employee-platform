using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.ScheduleBlocks;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.ScheduleBlocks;

public sealed class ScheduleBlockRemovalService
    : IScheduleBlockRemovalService
{
    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public ScheduleBlockRemovalService(
        AppDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<bool> DeactivateAsync(
        Guid blockId,
        CancellationToken cancellationToken = default)
    {
        var block = await _dbContext.ScheduleBlocks
            .SingleOrDefaultAsync(
                x => x.Id == blockId,
                cancellationToken);

        if (block is null)
        {
            return false;
        }

        if (!block.IsActive)
        {
            return true;
        }

        var currentUtc = _timeProvider.GetUtcNow().ToUniversalTime();

        var now = new DateTimeOffset(
            currentUtc.Ticks - currentUtc.Ticks % 10,
            TimeSpan.Zero);

        block.Deactivate(now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}