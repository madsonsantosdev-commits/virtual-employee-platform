namespace VirtualEmployee.Application.Appointments.CreateAppointment;

public enum CreateAppointmentError
{
    None,
    InvalidRequest,
    CustomerNotFound,
    LocationNotFound,
    BusinessNotFound,
    ProfessionalNotFound,
    ServiceNotFound,
    InactiveLocation,
    InactiveBusiness,
    InactiveProfessional,
    CustomerFromAnotherBusiness,
    ProfessionalFromAnotherBusiness,
    ProfessionalLocationNotFound,
    InactiveProfessionalLocation,
    InvalidServices,
    ServicesNotOffered,
    ProfessionalNotEligible,
    InvalidTotals,
    InvalidTimezone,
    SlotUnavailable,
    IdempotencyConflict
}