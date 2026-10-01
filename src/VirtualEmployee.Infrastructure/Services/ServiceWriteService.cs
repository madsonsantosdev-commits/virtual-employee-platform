using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Services;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Services;

public sealed class ServiceWriteService : IServiceWriteService
{
    private readonly AppDbContext _dbContext;

    public ServiceWriteService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> BusinessExistsAsync(
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Businesses
            .AsNoTracking()
            .AnyAsync(
                business => business.Id == businessId,
                cancellationToken);
    }

    public Task<Service?> GetByIdAsync(
        Guid serviceId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Services
            .SingleOrDefaultAsync(
                service => service.Id == serviceId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Service>> GetByIdsAsync(
        IReadOnlyCollection<Guid> serviceIds,
        CancellationToken cancellationToken = default)
    {
        if (serviceIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.Services
            .AsNoTracking()
            .Where(service => serviceIds.Contains(service.Id))
            .ToListAsync(cancellationToken);
    }

    public void Add(Service service)
    {
        _dbContext.Services.Add(service);
    }

    public void AddComponents(
        IEnumerable<ServiceComponent> components)
    {
        _dbContext.ServiceComponents.AddRange(components);
    }

    public async Task ReplaceComponentsAsync(
        Guid comboServiceId,
        IReadOnlyList<Guid> componentServiceIds,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var existingComponents =
            await _dbContext.ServiceComponents
                .Where(component =>
                    component.ComboServiceId == comboServiceId)
                .ToListAsync(cancellationToken);

        if (existingComponents.Count > 0)
        {
            _dbContext.ServiceComponents.RemoveRange(
                existingComponents);
        }

        // Flush the DELETEs first because the PK contains
        // TenantId + ComboServiceId + ComponentServiceId.
        // This prevents EF tracking conflicts when a component
        // remains in the combo with a different SortOrder.
        if (existingComponents.Count > 0)
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }

        if (componentServiceIds.Count == 0)
        {
            return;
        }

        var service = await _dbContext.Services
            .SingleAsync(
                service => service.Id == comboServiceId,
                cancellationToken);

        var newComponents = componentServiceIds.Select(
            (componentServiceId, index) =>
                new ServiceComponent(
                    service.TenantId,
                    comboServiceId,
                    componentServiceId,
                    index,
                    updatedAt));

        _dbContext.ServiceComponents.AddRange(newComponents);
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}