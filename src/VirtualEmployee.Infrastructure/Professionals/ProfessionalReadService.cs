using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Professionals;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Application.Professionals.GetProfessionals;
namespace VirtualEmployee.Infrastructure.Professionals;

public sealed class ProfessionalReadService : IProfessionalReadService
{
    private readonly AppDbContext _dbContext;

    public ProfessionalReadService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<IReadOnlyList<ProfessionalResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return GetAllAsync(
            new GetProfessionalsQuery(),
            cancellationToken);
    }

    public async Task<IReadOnlyList<ProfessionalResponse>> GetAllAsync(GetProfessionalsQuery query, CancellationToken cancellationToken = default)
    {
        var professionals = _dbContext.Professionals
            .AsNoTracking()
            .AsQueryable();

        if (query.Active is bool active)
        {
            professionals = professionals.Where(
                professional => professional.IsActive == active);
        }

        if (query.LocationId is Guid locationId)
        {
            professionals = professionals.Where(professional =>
                _dbContext.Locations.Any(location =>
                    location.Id == locationId &&
                    location.IsActive &&
                    location.BusinessId == professional.BusinessId) &&
                _dbContext.ProfessionalLocations.Any(link =>
                    link.ProfessionalId == professional.Id &&
                    link.LocationId == locationId &&
                    link.IsActive));
        }

        var serviceIds = query.ServiceIds?
            .Distinct()
            .ToArray() ?? [];

        foreach (var serviceId in serviceIds)
        {
            professionals = professionals.Where(professional =>
                _dbContext.Services.Any(service =>
                    service.Id == serviceId &&
                    service.IsActive &&
                    service.BusinessId == professional.BusinessId) &&
                _dbContext.ProfessionalServices.Any(link =>
                    link.ProfessionalId == professional.Id &&
                    link.ServiceId == serviceId &&
                    link.IsActive));

            if (query.LocationId is Guid serviceLocationId)
            {
                professionals = professionals.Where(professional =>
                    _dbContext.LocationServices.Any(link =>
                    link.LocationId == serviceLocationId &&
                    link.ServiceId == serviceId));
            }
        }

        return await professionals
            .OrderBy(professional => professional.Name)
            .ThenBy(professional => professional.Id)
            .Select(professional => new ProfessionalResponse(
                professional.Id,
                professional.BusinessId,
                professional.Name,
                professional.IsActive))
            .ToListAsync(cancellationToken);
    }

    public Task<ProfessionalResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Professionals
            .AsNoTracking()
            .Where(professional => professional.Id == id)
            .Select(professional => new ProfessionalResponse(
                professional.Id,
                professional.BusinessId,
                professional.Name,
                professional.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
    }
}