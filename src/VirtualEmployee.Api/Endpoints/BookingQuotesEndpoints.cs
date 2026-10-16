using VirtualEmployee.Application.BookingQuotes.GetBookingQuote;

namespace VirtualEmployee.Api.Endpoints;

public static class BookingQuotesEndpoints
{
    public static IEndpointRouteBuilder MapBookingQuotesEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/v1/booking/quote",
            async (
                BookingQuoteRequest request,
                GetBookingQuoteHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
                    new GetBookingQuoteQuery(
                        request.LocationId,
                        request.ServiceIds ?? Array.Empty<Guid>()),
                    cancellationToken);

                if (result.IsSuccess && result.Response is { } response)
                {
                    return Results.Ok(response);
                }

                return result.Error switch
                {
                    GetBookingQuoteError.InvalidRequest =>
                        Results.BadRequest(),

                    GetBookingQuoteError.LocationNotFound or
                    GetBookingQuoteError.BusinessNotFound or
                    GetBookingQuoteError.ServiceNotFound =>
                        Results.NotFound(),

                    GetBookingQuoteError.InactiveLocation or
                    GetBookingQuoteError.InactiveBusiness or
                    GetBookingQuoteError.InvalidServices or
                    GetBookingQuoteError.ServicesNotOffered or
                    GetBookingQuoteError.InvalidTotals =>
                        Results.Conflict(),

                    _ => Results.Problem(
                        title: "Booking quote failed",
                        statusCode:
                            StatusCodes.Status500InternalServerError)
                };
            })
            .WithTags("Booking Quotes");

        return app;
    }

    public sealed record BookingQuoteRequest(
        Guid LocationId,
        IReadOnlyList<Guid>? ServiceIds);
}