using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.BookingQuotes;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.BookingQuotes;

public sealed class BookingQuoteReadService : IBookingQuoteReadService
{
    private readonly AppDbContext _dbContext;

    public BookingQuoteReadService(AppDbContext dbContext)
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

        if (ids.Length == 0)
        {
            return false;
        }

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
}