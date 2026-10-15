namespace VirtualEmployee.Application.Customers.CreateCustomer;

public sealed record CreateCustomerCommand(
    Guid BusinessId,
    string Phone,
    string? Name = null,
    string? Email = null);