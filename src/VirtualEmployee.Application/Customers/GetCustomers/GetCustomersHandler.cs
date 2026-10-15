namespace VirtualEmployee.Application.Customers.GetCustomers;

public sealed class GetCustomersHandler
{
    private readonly ICustomerReadService _readService;

    public GetCustomersHandler(ICustomerReadService readService)
    {
        _readService = readService;
    }

    public Task<IReadOnlyList<CustomerResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        return _readService.GetAllAsync(cancellationToken);
    }
}