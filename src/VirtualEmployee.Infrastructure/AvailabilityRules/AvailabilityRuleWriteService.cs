using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.AvailabilityRules;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.AvailabilityRules;

public sealed class AvailabilityRuleWriteService
    : IAvailabilityRuleWriteService
{
    private readonly AppDbContext _dbContext;

    public AvailabilityRuleWriteService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Location?> GetLocationByIdAsync(
        Guid locationId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Locations.SingleOrDefaultAsync(
            location => location.Id == locationId,
            cancellationToken);
    }

    public Task<Professional?> GetProfessionalByIdAsync(
        Guid professionalId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Professionals.SingleOrDefaultAsync(
            professional => professional.Id == professionalId,
            cancellationToken);
    }

    public Task<ProfessionalLocation?> GetProfessionalLocationAsync(
        Guid locationId,
        Guid professionalId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.ProfessionalLocations.SingleOrDefaultAsync(
            link =>
                link.LocationId == locationId &&
                link.ProfessionalId == professionalId,
            cancellationToken);
    }

    public async Task ReplaceAsync(
        Guid locationId,
        Guid professionalId,
        IReadOnlyList<AvailabilityRuleInput> rules,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var existingRules = await _dbContext.AvailabilityRules
            .Where(rule =>
                rule.LocationId == locationId &&
                rule.ProfessionalId == professionalId)
            .ToListAsync(cancellationToken);

        var requestedWindows = rules
            .Select(rule => (
                rule.DayOfWeek,
                rule.StartTime,
                rule.EndTime))
            .ToHashSet();

        var existingWindows = existingRules
            .Select(rule => (
                rule.DayOfWeek,
                rule.StartTime,
                rule.EndTime))
            .ToHashSet();

        foreach (var existingRule in existingRules)
        {
            var shouldBeActive = requestedWindows.Contains((
                existingRule.DayOfWeek,
                existingRule.StartTime,
                existingRule.EndTime));

            existingRule.Update(
                existingRule.DayOfWeek,
                existingRule.StartTime,
                existingRule.EndTime,
                shouldBeActive,
                updatedAt);
        }

        var rulesToAdd = requestedWindows
            .Where(window => !existingWindows.Contains(window))
            .Select(window => new AvailabilityRule(
                Guid.NewGuid(),
                _dbContext.CurrentTenantId,
                locationId,
                professionalId,
                window.DayOfWeek,
                window.StartTime,
                window.EndTime,
                updatedAt))
            .ToArray();

        if (rulesToAdd.Length > 0)
        {
            await _dbContext.AvailabilityRules.AddRangeAsync(
                rulesToAdd,
                cancellationToken);
        }
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}