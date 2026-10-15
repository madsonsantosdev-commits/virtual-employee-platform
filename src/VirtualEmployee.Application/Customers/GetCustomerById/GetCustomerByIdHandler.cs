namespace VirtualEmployee.Application.Customers.GetCustomerById;

public sealed class GetCustomerByIdHandler
{
    private readonly ICustomerReadService _readService;

    public GetCustomerByIdHandler(ICustomerReadService readService)
    {
        _readService = readService;
    }

    public Task<CustomerResponse?> HandleAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        if (customerId == Guid.Empty)
        {
            return Task.FromResult<CustomerResponse?>(null);
        }

        return _readService.GetByIdAsync(
            customerId,
            cancellationToken);
    }
}