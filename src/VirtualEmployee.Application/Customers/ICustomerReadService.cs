namespace VirtualEmployee.Application.Customers;

public interface ICustomerReadService
{
    Task<IReadOnlyList<CustomerResponse>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<CustomerResponse?> GetByIdAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
}