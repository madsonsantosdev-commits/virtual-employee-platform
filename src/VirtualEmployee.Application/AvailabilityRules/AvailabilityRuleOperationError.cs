namespace VirtualEmployee.Application.AvailabilityRules;

public enum AvailabilityRuleOperationError
{
    None = 0,
    LocationNotFound = 1,
    ProfessionalNotFound = 2,
    ProfessionalFromAnotherBusiness = 3,
    ProfessionalLocationNotFound = 4,
    InactiveLocation = 5,
    InactiveProfessional = 6,
    InactiveProfessionalLocation = 7,
    InvalidDayOfWeek = 8,
    InvalidTimeWindow = 9,
    OverlappingRules = 10
}