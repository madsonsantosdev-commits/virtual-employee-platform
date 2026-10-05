using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Professionals;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Professionals;

public sealed class ProfessionalWriteService : IProfessionalWriteService
{
    private readonly AppDbContext _dbContext;

    public ProfessionalWriteService(AppDbContext dbContext)
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

    public Task<Professional?> GetByIdAsync(
        Guid professionalId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Professionals
            .SingleOrDefaultAsync(
                professional => professional.Id == professionalId,
                cancellationToken);
    }

    public async Task AddAsync(
        Professional professional,
        CancellationToken cancellationToken = default)
    {
        _dbContext.Professionals.Add(professional);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}