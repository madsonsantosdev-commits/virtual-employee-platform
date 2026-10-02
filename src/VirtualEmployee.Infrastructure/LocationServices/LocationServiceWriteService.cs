using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.LocationServices;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.LocationServices;

public sealed class LocationServiceWriteService
    : ILocationServiceWriteService
{
    private readonly AppDbContext _dbContext;

    public LocationServiceWriteService(
        AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Location?> GetLocationByIdAsync(
        Guid locationId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Locations
            .SingleOrDefaultAsync(
                location => location.Id == locationId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Service>> GetServicesByIdsAsync(
        IReadOnlyCollection<Guid> serviceIds,
        CancellationToken cancellationToken = default)
    {
        if (serviceIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.Services
            .Where(service =>
                serviceIds.Contains(service.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task ReplaceAsync(
        Guid locationId,
        IReadOnlyCollection<Guid> serviceIds,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        var existingLinks = await _dbContext.LocationServices
            .Where(locationService =>
                locationService.LocationId == locationId)
            .ToListAsync(cancellationToken);

        var requestedServiceIds =
            serviceIds.ToHashSet();

        var linksToRemove = existingLinks
            .Where(locationService =>
                !requestedServiceIds.Contains(
                    locationService.ServiceId))
            .ToArray();

        if (linksToRemove.Length > 0)
        {
            _dbContext.LocationServices.RemoveRange(
                linksToRemove);
        }

        var existingServiceIds = existingLinks
            .Select(locationService =>
                locationService.ServiceId)
            .ToHashSet();

        var linksToAdd = requestedServiceIds
            .Where(serviceId =>
                !existingServiceIds.Contains(serviceId))
            .Select(serviceId =>
                new LocationService(
                    _dbContext.CurrentTenantId,
                    locationId,
                    serviceId,
                    createdAt))
            .ToArray();

        if (linksToAdd.Length > 0)
        {
            await _dbContext.LocationServices.AddRangeAsync(
                linksToAdd,
                cancellationToken);
        }
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(
            cancellationToken);
    }
}