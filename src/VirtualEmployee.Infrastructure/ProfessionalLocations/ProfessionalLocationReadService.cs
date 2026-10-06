using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Locations;
using VirtualEmployee.Application.ProfessionalLocations;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.ProfessionalLocations;

public sealed class ProfessionalLocationReadService
    : IProfessionalLocationReadService
{
    private readonly AppDbContext _dbContext;

    public ProfessionalLocationReadService(
        AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<LocationResponse>> GetByProfessionalIdAsync(
        Guid professionalId,
        CancellationToken cancellationToken = default)
    {
        var activeLocationIds = _dbContext.ProfessionalLocations
            .AsNoTracking()
            .Where(link =>
                link.ProfessionalId == professionalId &&
                link.IsActive)
            .Select(link => link.LocationId);

        return await _dbContext.Locations
            .AsNoTracking()
            .Where(location =>
                activeLocationIds.Contains(location.Id))
            .OrderBy(location => location.Name)
            .ThenBy(location => location.Id)
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
}