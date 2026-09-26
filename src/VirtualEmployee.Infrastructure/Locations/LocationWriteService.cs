using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Locations;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Locations;

public sealed class LocationWriteService : ILocationWriteService
{
    private readonly AppDbContext _dbContext;

    public LocationWriteService(AppDbContext dbContext)
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

    public Task<Location?> GetByIdAsync(
        Guid locationId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Locations
            .SingleOrDefaultAsync(
                location => location.Id == locationId,
                cancellationToken);
    }

    public async Task AddAsync(
        Location location,
        CancellationToken cancellationToken = default)
    {
        _dbContext.Locations.Add(location);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}