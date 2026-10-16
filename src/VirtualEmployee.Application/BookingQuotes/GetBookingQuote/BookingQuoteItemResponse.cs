namespace VirtualEmployee.Application.BookingQuotes.GetBookingQuote;

public sealed record BookingQuoteItemResponse(
    Guid ServiceId,
    string Name,
    decimal Price,
    int DurationMinutes);