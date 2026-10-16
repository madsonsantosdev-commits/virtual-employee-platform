namespace VirtualEmployee.Application.BookingQuotes.GetBookingQuote;

public sealed record BookingQuoteResponse(
    Guid LocationId,
    IReadOnlyList<BookingQuoteItemResponse> Items,
    decimal TotalPrice,
    int TotalDurationMinutes);