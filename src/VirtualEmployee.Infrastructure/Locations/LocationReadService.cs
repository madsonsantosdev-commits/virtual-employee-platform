using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Locations;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Locations;

public sealed class LocationReadService : ILocationReadService
{
    private readonly AppDbContext _dbContext;

    public LocationReadService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<LocationResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Locations
            .AsNoTracking()
            .OrderBy(location => location.Name)
            .Select(location => new LocationResponse(
                location.Id,
                location.BusinessId,
                location.Name,
                location.Phone,
                location.Address,
                location.CountryCode,
                location.Timezone,
                location.IsActive))
            .ToListAsync(cancellationToken);
    }

    public Task<LocationResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Locations
            .AsNoTracking()
            .Where(location => location.Id == id)
            .Select(location => new LocationResponse(
                location.Id,
                location.BusinessId,
                location.Name,
                location.Phone,
                location.Address,
                location.CountryCode,
                location.Timezone,
                location.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
    }
}