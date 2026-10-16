namespace VirtualEmployee.Application.BookingQuotes.GetBookingQuote;

public sealed record GetBookingQuoteQuery(
    Guid LocationId,
    IReadOnlyList<Guid> ServiceIds);