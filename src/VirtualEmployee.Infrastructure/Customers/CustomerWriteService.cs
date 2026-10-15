using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Customers;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.Customers;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Customers;

public sealed class CustomerWriteService : ICustomerWriteService
{
    private readonly AppDbContext _dbContext;

    public CustomerWriteService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
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

    public Task AddAsync(
        Customer customer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _dbContext.Customers.Add(customer);

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}