namespace VirtualEmployee.Application.LocationServices;

public sealed record LocationServiceOperationResult(
    LocationServiceOperationError Error)
{
    public bool IsSuccess =>
        Error == LocationServiceOperationError.None;

    public static LocationServiceOperationResult Success()
    {
        return new LocationServiceOperationResult(
            LocationServiceOperationError.None);
    }

    public static LocationServiceOperationResult Failure(
        LocationServiceOperationError error)
    {
        return new LocationServiceOperationResult(error);
    }
}