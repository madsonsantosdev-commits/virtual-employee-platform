using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.Customers;

namespace VirtualEmployee.Application.Customers;

public interface ICustomerWriteService
{
    Task<Business?> GetBusinessByIdAsync(
        Guid businessId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Customer customer,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}