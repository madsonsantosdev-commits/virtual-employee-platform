using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.LocationServices;
using VirtualEmployee.Application.Services;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.LocationServices;

public sealed class LocationServiceReadService
    : ILocationServiceReadService
{
    private readonly AppDbContext _dbContext;

    public LocationServiceReadService(
        AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ServiceResponse>> GetByLocationIdAsync(
        Guid locationId,
        CancellationToken cancellationToken = default)
    {
        var serviceIds = await _dbContext.LocationServices
            .AsNoTracking()
            .Where(locationService =>
                locationService.LocationId == locationId)
            .Select(locationService =>
                locationService.ServiceId)
            .ToListAsync(cancellationToken);

        if (serviceIds.Count == 0)
        {
            return [];
        }

        var services = await _dbContext.Services
            .AsNoTracking()
            .Where(service =>
                serviceIds.Contains(service.Id))
            .OrderBy(service => service.Name)
            .Select(service => new
            {
                service.Id,
                service.BusinessId,
                service.Name,
                service.ServiceType,
                service.Price,
                service.DurationMinutes,
                service.IsActive
            })
            .ToListAsync(cancellationToken);

        var components = await _dbContext.ServiceComponents
            .AsNoTracking()
            .Where(component =>
                serviceIds.Contains(component.ComboServiceId))
            .OrderBy(component => component.SortOrder)
            .Select(component => new
            {
                component.ComboServiceId,
                component.ComponentServiceId
            })
            .ToListAsync(cancellationToken);

        var componentsByCombo = components
            .GroupBy(component => component.ComboServiceId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Guid>)group
                    .Select(component =>
                        component.ComponentServiceId)
                    .ToArray());

        return services
            .Select(service =>
                new ServiceResponse(
                    service.Id,
                    service.BusinessId,
                    service.Name,
                    service.ServiceType,
                    service.Price,
                    service.DurationMinutes,
                    service.IsActive,
                    componentsByCombo.TryGetValue(
                        service.Id,
                        out var componentIds)
                        ? componentIds
                        : []))
            .ToArray();
    }
}