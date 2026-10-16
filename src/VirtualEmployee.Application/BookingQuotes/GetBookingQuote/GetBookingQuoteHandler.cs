namespace VirtualEmployee.Application.BookingQuotes.GetBookingQuote;

public sealed class GetBookingQuoteHandler
{
    private readonly IBookingQuoteReadService _readService;

    public GetBookingQuoteHandler(IBookingQuoteReadService readService)
    {
        _readService = readService;
    }

    public async Task<GetBookingQuoteResult> HandleAsync(
        GetBookingQuoteQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.LocationId == Guid.Empty ||
            query.ServiceIds is null ||
            query.ServiceIds.Count == 0)
        {
            return Failure(GetBookingQuoteError.InvalidRequest);
        }

        var serviceIds = query.ServiceIds.ToArray();

        if (serviceIds.Any(id => id == Guid.Empty) ||
            serviceIds.Distinct().Count() != serviceIds.Length)
        {
            return Failure(GetBookingQuoteError.InvalidRequest);
        }

        var location = await _readService.GetLocationByIdAsync(
            query.LocationId,
            cancellationToken);

        if (location is null)
        {
            return Failure(GetBookingQuoteError.LocationNotFound);
        }

        if (!location.IsActive)
        {
            return Failure(GetBookingQuoteError.InactiveLocation);
        }

        var business = await _readService.GetBusinessByIdAsync(
            location.BusinessId,
            cancellationToken);

        if (business is null)
        {
            return Failure(GetBookingQuoteError.BusinessNotFound);
        }

        if (!business.IsActive)
        {
            return Failure(GetBookingQuoteError.InactiveBusiness);
        }

        var services = await _readService.GetServicesByIdsAsync(
            serviceIds,
            cancellationToken);

        if (services.Count != serviceIds.Length ||
            !services.Select(service => service.Id)
                .ToHashSet()
                .SetEquals(serviceIds))
        {
            return Failure(GetBookingQuoteError.ServiceNotFound);
        }

        if (services.Any(service =>
                !service.IsActive ||
                service.BusinessId != business.Id))
        {
            return Failure(GetBookingQuoteError.InvalidServices);
        }

        var offered = await _readService.AreServicesOfferedAtLocationAsync(
            location.Id,
            serviceIds,
            cancellationToken);

        if (!offered)
        {
            return Failure(GetBookingQuoteError.ServicesNotOffered);
        }

        var servicesById = services.ToDictionary(service => service.Id);

        var items = serviceIds
            .Select(id => servicesById[id])
            .Select(service => new BookingQuoteItemResponse(
                service.Id,
                service.Name,
                service.Price,
                service.DurationMinutes))
            .ToArray();

        if (items.Any(item =>
                item.Price < 0 ||
                item.DurationMinutes <= 0))
        {
            return Failure(GetBookingQuoteError.InvalidTotals);
        }

        decimal totalPrice;
        long totalDuration;

        try
        {
            totalPrice = items.Sum(item => item.Price);
            totalDuration = items.Sum(
                item => (long)item.DurationMinutes);
        }
        catch (OverflowException)
        {
            return Failure(GetBookingQuoteError.InvalidTotals);
        }

        if (totalDuration <= 0 || totalDuration > int.MaxValue)
        {
            return Failure(GetBookingQuoteError.InvalidTotals);
        }

        var response = new BookingQuoteResponse(
            location.Id,
            items,
            totalPrice,
            (int)totalDuration);

        return GetBookingQuoteResult.Success(response);
    }

    private static GetBookingQuoteResult Failure(
        GetBookingQuoteError error)
        => GetBookingQuoteResult.Failure(error);
}