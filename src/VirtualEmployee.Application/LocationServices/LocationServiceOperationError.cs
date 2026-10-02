namespace VirtualEmployee.Application.LocationServices;

public enum LocationServiceOperationError
{
    None = 0,
    LocationNotFound = 1,
    ServiceNotFound = 2,
    ServiceFromAnotherBusiness = 3,
    DuplicateService = 4
}