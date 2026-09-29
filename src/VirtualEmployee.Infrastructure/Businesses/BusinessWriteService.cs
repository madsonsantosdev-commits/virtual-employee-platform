using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Businesses;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Businesses;

public sealed class BusinessWriteService : IBusinessWriteService
{
    private readonly AppDbContext _dbContext;

    public BusinessWriteService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Business?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Businesses
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<bool> IsBusinessTypeActiveAsync(
        Guid businessTypeId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.BusinessTypes
            .AnyAsync(
                businessType =>
                    businessType.Id == businessTypeId &&
                    businessType.IsActive,
                cancellationToken);
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}