namespace VirtualEmployee.Application.Customers;

public sealed record CustomerResponse(
    Guid Id,
    Guid BusinessId,
    string? Name,
    string Phone,
    string? Email);