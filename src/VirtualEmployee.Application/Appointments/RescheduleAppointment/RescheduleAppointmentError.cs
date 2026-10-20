namespace VirtualEmployee.Application.Appointments.RescheduleAppointment;

public enum RescheduleAppointmentError
{
    None,
    InvalidRequest,
    AppointmentNotFound,
    InvalidState,
    ReservationExpired,
    LocationNotFound,
    BusinessNotFound,
    ProfessionalNotFound,
    ServiceNotFound,
    InactiveLocation,
    InactiveBusiness,
    InactiveProfessional,
    LocationFromAnotherBusiness,
    ProfessionalFromAnotherBusiness,
    ProfessionalLocationNotFound,
    InactiveProfessionalLocation,
    InvalidServices,
    ServicesNotOffered,
    ProfessionalNotEligible,
    InvalidTimezone,
    SlotUnavailable,
    IdempotencyConflict,
    ConcurrencyConflict
}