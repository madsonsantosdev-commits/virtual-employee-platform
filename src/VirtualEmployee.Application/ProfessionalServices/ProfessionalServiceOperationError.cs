namespace VirtualEmployee.Application.ProfessionalServices;

public enum ProfessionalServiceOperationError
{
    None = 0,
    ProfessionalNotFound = 1,
    ServiceNotFound = 2,
    ServiceFromAnotherBusiness = 3,
    DuplicateService = 4
}