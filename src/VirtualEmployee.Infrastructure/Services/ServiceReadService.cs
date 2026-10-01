using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Services;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Services;

public sealed class ServiceReadService : IServiceReadService
{
    private readonly AppDbContext _dbContext;

    public ServiceReadService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ServiceResponse>> GetAllAsync(
        bool? active = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Services
            .AsNoTracking()
            .AsQueryable();

        if (active.HasValue)
        {
            query = query.Where(
                service => service.IsActive == active.Value);
        }

        var services = await query
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

        var serviceIds = services
            .Select(service => service.Id)
            .ToArray();

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
                    .Select(component => component.ComponentServiceId)
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

    public async Task<ServiceResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var service = await _dbContext.Services
            .AsNoTracking()
            .Where(service => service.Id == id)
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
            .SingleOrDefaultAsync(cancellationToken);

        if (service is null)
        {
            return null;
        }

        var componentIds = await _dbContext.ServiceComponents
            .AsNoTracking()
            .Where(component =>
                component.ComboServiceId == service.Id)
            .OrderBy(component => component.SortOrder)
            .Select(component => component.ComponentServiceId)
            .ToListAsync(cancellationToken);

        return new ServiceResponse(
            service.Id,
            service.BusinessId,
            service.Name,
            service.ServiceType,
            service.Price,
            service.DurationMinutes,
            service.IsActive,
            componentIds);
    }
}