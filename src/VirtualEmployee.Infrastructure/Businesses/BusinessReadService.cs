using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Businesses;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Businesses;

public sealed class BusinessReadService : IBusinessReadService
{
    private readonly AppDbContext _dbContext;

    public BusinessReadService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<BusinessResponse?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Businesses
            .AsNoTracking()
            .Select(business => new BusinessResponse(
                business.Id,
                business.BusinessTypeId,
                business.Name,
                business.SlotIntervalMinutes,
                business.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
    }
}