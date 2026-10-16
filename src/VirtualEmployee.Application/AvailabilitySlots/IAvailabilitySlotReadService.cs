using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Application.ScheduleBlocks;
namespace VirtualEmployee.Application.AvailabilitySlots;

public interface IAvailabilitySlotReadService
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

    Task<IReadOnlyList<AvailabilityWindow>> GetEligibleWindowsAsync(
        Guid locationId,
        Guid businessId,
        IReadOnlyList<Guid> serviceIds,
        DayOfWeek dayOfWeek,
        Guid? professionalId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScheduleBlockResponse>> GetBlocksAsync(
        Guid locationId,
        DateTimeOffset from,
        DateTimeOffset to,
        Guid? professionalId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OccupiedAppointmentWindow>> GetOccupiedAppointmentsAsync(
        IReadOnlyList<Guid> professionalIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}