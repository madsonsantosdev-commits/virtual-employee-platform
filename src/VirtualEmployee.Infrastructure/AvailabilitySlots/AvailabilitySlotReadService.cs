using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.AvailabilitySlots;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Application.ScheduleBlocks;
using VirtualEmployee.Infrastructure.ScheduleBlocks;
namespace VirtualEmployee.Infrastructure.AvailabilitySlots;

public sealed class AvailabilitySlotReadService
    : IAvailabilitySlotReadService
{
    private readonly AppDbContext _dbContext;

    public AvailabilitySlotReadService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Location?> GetLocationByIdAsync(
        Guid locationId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Locations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                location => location.Id == locationId,
                cancellationToken);
    }

    public Task<Business?> GetBusinessByIdAsync(
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Businesses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                business => business.Id == businessId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Service>> GetServicesByIdsAsync(
        IReadOnlyList<Guid> serviceIds,
        CancellationToken cancellationToken = default)
    {
        var ids = serviceIds.ToArray();

        return await _dbContext.Services
            .AsNoTracking()
            .Where(service => ids.Contains(service.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> AreServicesOfferedAtLocationAsync(
        Guid locationId,
        IReadOnlyList<Guid> serviceIds,
        CancellationToken cancellationToken = default)
    {
        var ids = serviceIds.Distinct().ToArray();

        var offeredCount = await _dbContext.LocationServices
            .AsNoTracking()
            .Where(link =>
                link.LocationId == locationId &&
                ids.Contains(link.ServiceId))
            .Select(link => link.ServiceId)
            .Distinct()
            .CountAsync(cancellationToken);

        return offeredCount == ids.Length;
    }

    public async Task<IReadOnlyList<AvailabilityWindow>> GetEligibleWindowsAsync(
        Guid locationId,
        Guid businessId,
        IReadOnlyList<Guid> serviceIds,
        DayOfWeek dayOfWeek,
        Guid? professionalId,
        CancellationToken cancellationToken = default)
    {
        var professionals = _dbContext.Professionals
            .AsNoTracking()
            .Where(professional =>
                professional.BusinessId == businessId &&
                professional.IsActive &&
                _dbContext.ProfessionalLocations.Any(link =>
                    link.ProfessionalId == professional.Id &&
                    link.LocationId == locationId &&
                    link.IsActive));

        if (professionalId is Guid selectedProfessionalId)
        {
            professionals = professionals.Where(
                professional => professional.Id == selectedProfessionalId);
        }

        foreach (var serviceId in serviceIds.Distinct())
        {
            professionals = professionals.Where(professional =>
                _dbContext.ProfessionalServices.Any(link =>
                    link.ProfessionalId == professional.Id &&
                    link.ServiceId == serviceId &&
                    link.IsActive));
        }

        var eligibleProfessionalIds = professionals
            .Select(professional => professional.Id);

        return await _dbContext.AvailabilityRules
            .AsNoTracking()
            .Where(rule =>
                rule.LocationId == locationId &&
                rule.DayOfWeek == dayOfWeek &&
                rule.IsActive &&
                eligibleProfessionalIds.Contains(rule.ProfessionalId))
            .OrderBy(rule => rule.ProfessionalId)
            .ThenBy(rule => rule.StartTime)
            .ThenBy(rule => rule.EndTime)
            .ThenBy(rule => rule.Id)
            .Select(rule => new AvailabilityWindow(
                rule.ProfessionalId,
                rule.StartTime,
                rule.EndTime))
            .ToListAsync(cancellationToken);
    }
    public Task<IReadOnlyList<ScheduleBlockResponse>> GetBlocksAsync(
        Guid locationId,
        DateTimeOffset from,
        DateTimeOffset to,
        Guid? professionalId,
        CancellationToken cancellationToken = default)
    {
        return new ScheduleBlockReadService(_dbContext)
            .GetByPeriodAsync(
                locationId,
                from,
                to,
                professionalId,
                cancellationToken);
    }
}