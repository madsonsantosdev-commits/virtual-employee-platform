using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Application.BookingQuotes;

public interface IBookingQuoteReadService
{
    Task<Location?> GetLocationByIdAsync(
        Guid locationId,
        CancellationToken cancellationToken = default);

    Task<Business?> GetBusinessByIdAsync(
        Guid businessId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Service>> GetServicesByIdsAsync(
        IReadOnlyList<Guid> serviceIds,
        CancellationToken cancellationToken = default);

    Task<bool> AreServicesOfferedAtLocationAsync(
        Guid locationId,
        IReadOnlyList<Guid> serviceIds,
        CancellationToken cancellationToken = default);
}