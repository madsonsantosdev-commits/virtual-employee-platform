using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.AvailabilityRules;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.AvailabilityRules;

public sealed class AvailabilityRuleReadService
    : IAvailabilityRuleReadService
{
    private readonly AppDbContext _dbContext;

    public AvailabilityRuleReadService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<AvailabilityRuleResponse>>
        GetByProfessionalLocationAsync(
            Guid locationId,
            Guid professionalId,
            CancellationToken cancellationToken = default)
    {
        return await _dbContext.AvailabilityRules
            .AsNoTracking()
            .Where(rule =>
                rule.LocationId == locationId &&
                rule.ProfessionalId == professionalId &&
                rule.IsActive)
            .OrderBy(rule => rule.DayOfWeek)
            .ThenBy(rule => rule.StartTime)
            .ThenBy(rule => rule.EndTime)
            .ThenBy(rule => rule.Id)
            .Select(rule => new AvailabilityRuleResponse(
                rule.Id,
                (int)rule.DayOfWeek,
                rule.StartTime,
                rule.EndTime))
            .ToListAsync(cancellationToken);
    }
}