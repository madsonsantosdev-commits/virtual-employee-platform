using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Professionals;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Professionals;

public sealed class ProfessionalReadService : IProfessionalReadService
{
    private readonly AppDbContext _dbContext;

    public ProfessionalReadService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ProfessionalResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Professionals
            .AsNoTracking()
            .OrderBy(professional => professional.Name)
            .ThenBy(professional => professional.Id)
            .Select(professional => new ProfessionalResponse(
                professional.Id,
                professional.BusinessId,
                professional.Name,
                professional.IsActive))
            .ToListAsync(cancellationToken);
    }

    public Task<ProfessionalResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
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