namespace VirtualEmployee.Application.BookingQuotes.GetBookingQuote;

public enum GetBookingQuoteError
{
    None,
    InvalidRequest,
    LocationNotFound,
    InactiveLocation,
    BusinessNotFound,
    InactiveBusiness,
    ServiceNotFound,
    InvalidServices,
    ServicesNotOffered,
    InvalidTotals
}