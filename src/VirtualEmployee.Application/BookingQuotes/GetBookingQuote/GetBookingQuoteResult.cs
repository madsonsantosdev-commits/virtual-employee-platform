namespace VirtualEmployee.Application.BookingQuotes.GetBookingQuote;

public sealed record GetBookingQuoteResult(
    GetBookingQuoteError Error,
    BookingQuoteResponse? Response)
{
    public bool IsSuccess => Error == GetBookingQuoteError.None;

    public static GetBookingQuoteResult Success(
        BookingQuoteResponse response)
        => new(GetBookingQuoteError.None, response);

    public static GetBookingQuoteResult Failure(
        GetBookingQuoteError error)
        => new(error, null);
}