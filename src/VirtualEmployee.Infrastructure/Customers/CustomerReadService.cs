using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Customers;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Customers;

public sealed class CustomerReadService : ICustomerReadService
{
    private readonly AppDbContext _dbContext;

    public CustomerReadService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<CustomerResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Customers
            .AsNoTracking()
            .OrderBy(customer => customer.Name)
            .ThenBy(customer => customer.Id)
            .Select(customer => new CustomerResponse(
                customer.Id,
                customer.BusinessId,
                customer.Name,
                customer.Phone,
                customer.Email))
            .ToListAsync(cancellationToken);
    }

    public Task<CustomerResponse?> GetByIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Customers
            .AsNoTracking()
            .Where(customer => customer.Id == customerId)
            .Select(customer => new CustomerResponse(
                customer.Id,
                customer.BusinessId,
                customer.Name,
                customer.Phone,
                customer.Email))
            .SingleOrDefaultAsync(cancellationToken);
    }
}