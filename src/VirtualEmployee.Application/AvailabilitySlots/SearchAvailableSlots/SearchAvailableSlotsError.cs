namespace VirtualEmployee.Application.AvailabilitySlots.SearchAvailableSlots;

public enum SearchAvailableSlotsError
{
    None,
    InvalidRequest,
    LocationNotFound,
    BusinessNotFound,
    InactiveLocation,
    InactiveBusiness,
    ServiceNotFound,
    InvalidServices,
    ServicesNotOffered,
    InvalidTimezone
}