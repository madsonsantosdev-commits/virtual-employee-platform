namespace VirtualEmployee.Application.ScheduleBlocks.CreateScheduleBlock;

public enum CreateScheduleBlockError
{
    None,
    InvalidRequest,
    LocationNotFound,
    BusinessNotFound,
    ProfessionalNotFound,
    ProfessionalLocationNotFound,
    ProfessionalFromAnotherBusiness,
    InactiveLocation,
    InactiveBusiness,
    InactiveProfessional,
    InactiveProfessionalLocation,
    IdempotencyConflict
}