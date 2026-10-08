using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.ProfessionalServices;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.ProfessionalServices;

public sealed class ProfessionalServiceWriteService
    : IProfessionalServiceWriteService
{
    private readonly AppDbContext _dbContext;

    public ProfessionalServiceWriteService(
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

    public async Task<IReadOnlyList<Service>> GetServicesByIdsAsync(
        IReadOnlyCollection<Guid> serviceIds,
        CancellationToken cancellationToken = default)
    {
        if (serviceIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.Services
            .Where(service => serviceIds.Contains(service.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task ReplaceAsync(
        Guid professionalId,
        IReadOnlyCollection<Guid> serviceIds,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        var existingLinks = await _dbContext.ProfessionalServices
            .Where(link => link.ProfessionalId == professionalId)
            .ToListAsync(cancellationToken);

        var requestedServiceIds = serviceIds.ToHashSet();

        foreach (var link in existingLinks)
        {
            var shouldBeActive = requestedServiceIds.Contains(
                link.ServiceId);

            if (link.IsActive != shouldBeActive)
            {
                link.Update(shouldBeActive, updatedAt);
            }
        }

        var existingServiceIds = existingLinks
            .Select(link => link.ServiceId)
            .ToHashSet();

        var linksToAdd = requestedServiceIds
            .Where(serviceId => !existingServiceIds.Contains(serviceId))
            .Select(serviceId =>
                new ProfessionalService(
                    _dbContext.CurrentTenantId,
                    professionalId,
                    serviceId,
                    updatedAt))
            .ToArray();

        if (linksToAdd.Length > 0)
        {
            await _dbContext.ProfessionalServices.AddRangeAsync(
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