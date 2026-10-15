using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.ScheduleBlocks;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.ScheduleBlocks;

public sealed class ScheduleBlockReadService : IScheduleBlockReadService
{
    private readonly AppDbContext _dbContext;

    public ScheduleBlockReadService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> LocationExistsAsync(
        Guid locationId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Locations
            .AsNoTracking()
            .AnyAsync(
                location => location.Id == locationId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<ScheduleBlockResponse>> GetByPeriodAsync(
        Guid locationId,
        DateTimeOffset from,
        DateTimeOffset to,
        Guid? professionalId,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        var blocks = _dbContext.ScheduleBlocks
            .AsNoTracking()
            .Where(block =>
                block.LocationId == locationId &&
                block.IsActive &&
                block.StartsAt < toUtc &&
                fromUtc < block.EndsAt);

        if (professionalId is Guid selectedProfessionalId)
        {
            blocks = blocks.Where(block =>
                block.ProfessionalId == null ||
                block.ProfessionalId == selectedProfessionalId);
        }

        return await blocks
            .OrderBy(block => block.StartsAt)
            .ThenBy(block => block.Id)
            .Select(block => new ScheduleBlockResponse(
                block.Id,
                block.LocationId,
                block.ProfessionalId,
                block.StartsAt,
                block.EndsAt,
                block.IsActive,
                block.CreatedAt,
                block.UpdatedAt))
            .ToListAsync(cancellationToken);
    }
}