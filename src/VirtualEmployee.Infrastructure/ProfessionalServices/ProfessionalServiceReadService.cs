using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.ProfessionalServices;
using VirtualEmployee.Application.Services;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.ProfessionalServices;

public sealed class ProfessionalServiceReadService
    : IProfessionalServiceReadService
{
    private readonly AppDbContext _dbContext;

    public ProfessionalServiceReadService(
        AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ServiceResponse>> GetByProfessionalIdAsync(
        Guid professionalId,
        CancellationToken cancellationToken = default)
    {
        var serviceIds = _dbContext.ProfessionalServices
            .AsNoTracking()
            .Where(link =>
                link.ProfessionalId == professionalId &&
                link.IsActive)
            .Select(link => link.ServiceId);

        var services = await _dbContext.Services
            .AsNoTracking()
            .Where(service => serviceIds.Contains(service.Id))
            .OrderBy(service => service.Name)
            .ThenBy(service => service.Id)
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

        if (services.Count == 0)
        {
            return [];
        }

        var returnedServiceIds = services
            .Select(service => service.Id)
            .ToArray();

        var components = await _dbContext.ServiceComponents
            .AsNoTracking()
            .Where(component =>
                returnedServiceIds.Contains(component.ComboServiceId))
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
}