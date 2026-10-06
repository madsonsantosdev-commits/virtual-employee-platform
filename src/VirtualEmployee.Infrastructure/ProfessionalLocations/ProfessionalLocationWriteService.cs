using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.ProfessionalLocations;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.ProfessionalLocations;

public sealed class ProfessionalLocationWriteService
    : IProfessionalLocationWriteService
{
    private readonly AppDbContext _dbContext;

    public ProfessionalLocationWriteService(
        AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Professional?> GetProfessionalByIdAsync(
        Guid professionalId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Professionals
            .SingleOrDefaultAsync(
                professional => professional.Id == professionalId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Location>> GetLocationsByIdsAsync(
        IReadOnlyCollection<Guid> locationIds,
        CancellationToken cancellationToken = default)
    {
        if (locationIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.Locations
            .Where(location => locationIds.Contains(location.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task ReplaceAsync(
        Guid professionalId,
        IReadOnlyCollection<Guid> locationIds,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var existingLinks = await _dbContext.ProfessionalLocations
            .Where(link => link.ProfessionalId == professionalId)
            .ToListAsync(cancellationToken);

        var requestedLocationIds = locationIds.ToHashSet();

        foreach (var link in existingLinks)
        {
            var shouldBeActive =
                requestedLocationIds.Contains(link.LocationId);

            if (link.IsActive != shouldBeActive)
            {
                link.Update(shouldBeActive, updatedAt);
            }
        }

        var existingLocationIds = existingLinks
            .Select(link => link.LocationId)
            .ToHashSet();

        var linksToAdd = requestedLocationIds
            .Where(locationId =>
                !existingLocationIds.Contains(locationId))
            .Select(locationId =>
                new ProfessionalLocation(
                    _dbContext.CurrentTenantId,
                    professionalId,
                    locationId,
                    updatedAt))
            .ToArray();

        if (linksToAdd.Length > 0)
        {
            await _dbContext.ProfessionalLocations.AddRangeAsync(
                linksToAdd,
                cancellationToken);
        }
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}