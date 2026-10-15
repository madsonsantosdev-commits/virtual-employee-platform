namespace VirtualEmployee.Application.Customers.CreateCustomer;

public sealed record CreateCustomerResult(
    CreateCustomerError Error,
    CustomerResponse? Response)
{
    public bool IsSuccess => Error == CreateCustomerError.None;

    public static CreateCustomerResult Success(
        CustomerResponse response)
        => new(CreateCustomerError.None, response);

    public static CreateCustomerResult Failure(
        CreateCustomerError error)
        => new(error, null);
}