namespace VirtualEmployee.Application.Services;

public sealed record ServiceOperationResult(
    ServiceResponse? Service,
    ServiceOperationError Error)
{
    public bool IsSuccess =>
        Error == ServiceOperationError.None &&
        Service is not null;

    public static ServiceOperationResult Success(
        ServiceResponse service)
    {
        return new ServiceOperationResult(
            service,
            ServiceOperationError.None);
    }

    public static ServiceOperationResult Failure(
        ServiceOperationError error)
    {
        return new ServiceOperationResult(
            null,
            error);
    }
}