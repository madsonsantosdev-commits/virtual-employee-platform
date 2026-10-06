namespace VirtualEmployee.Application.ProfessionalLocations;

public enum ProfessionalLocationOperationError
{
    None = 0,
    ProfessionalNotFound = 1,
    LocationNotFound = 2,
    LocationFromAnotherBusiness = 3,
    DuplicateLocation = 4
}