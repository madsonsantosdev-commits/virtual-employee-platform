using VirtualEmployee.Domain.Scheduling;

namespace VirtualEmployee.Application.AvailabilitySlots.SearchAvailableSlots;

public sealed class SearchAvailableSlotsHandler
{
    private readonly IAvailabilitySlotReadService _readService;
    private readonly TimeProvider _timeProvider;

    public SearchAvailableSlotsHandler(
        IAvailabilitySlotReadService readService,
        TimeProvider? timeProvider = null)
    {
        _readService = readService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SearchAvailableSlotsResult> HandleAsync(
        SearchAvailableSlotsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.LocationId == Guid.Empty ||
            query.ProfessionalId == Guid.Empty ||
            query.Date == DateOnly.MinValue ||
            query.ServiceIds is null ||
            query.ServiceIds.Count == 0 ||
            query.ServiceIds.Any(id => id == Guid.Empty) ||
            query.ServiceIds.Distinct().Count() != query.ServiceIds.Count)
        {
            return Failure(SearchAvailableSlotsError.InvalidRequest);
        }

        var location = await _readService.GetLocationByIdAsync(
            query.LocationId,
            cancellationToken);

        if (location is null)
        {
            return Failure(SearchAvailableSlotsError.LocationNotFound);
        }

        if (!location.IsActive)
        {
            return Failure(SearchAvailableSlotsError.InactiveLocation);
        }

        var business = await _readService.GetBusinessByIdAsync(
            location.BusinessId,
            cancellationToken);

        if (business is null)
        {
            return Failure(SearchAvailableSlotsError.BusinessNotFound);
        }

        if (!business.IsActive)
        {
            return Failure(SearchAvailableSlotsError.InactiveBusiness);
        }

        var services = await _readService.GetServicesByIdsAsync(
            query.ServiceIds,
            cancellationToken);

        if (services.Count != query.ServiceIds.Count ||
            !services.Select(service => service.Id)
                .ToHashSet()
                .SetEquals(query.ServiceIds))
        {
            return Failure(SearchAvailableSlotsError.ServiceNotFound);
        }

        if (services.Any(service =>
                !service.IsActive ||
                service.BusinessId != business.Id))
        {
            return Failure(SearchAvailableSlotsError.InvalidServices);
        }

        var totalDuration = services.Sum(
            service => (long)service.DurationMinutes);

        if (totalDuration <= 0 || totalDuration > int.MaxValue)
        {
            return Failure(SearchAvailableSlotsError.InvalidServices);
        }

        var offered = await _readService.AreServicesOfferedAtLocationAsync(
            location.Id,
            query.ServiceIds,
            cancellationToken);

        if (!offered)
        {
            return Failure(SearchAvailableSlotsError.ServicesNotOffered);
        }

        TimeZoneInfo timezone;

        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(
                location.Timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return Failure(SearchAvailableSlotsError.InvalidTimezone);
        }
        catch (InvalidTimeZoneException)
        {
            return Failure(SearchAvailableSlotsError.InvalidTimezone);
        }

        var windows = await _readService.GetEligibleWindowsAsync(
            location.Id,
            business.Id,
            query.ServiceIds,
            query.Date.DayOfWeek,
            query.ProfessionalId,
            cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var slots = new List<AvailableSlotResponse>();

        foreach (var professionalWindows in windows.GroupBy(
                     window => window.ProfessionalId))
        {
            var localSlots = SlotCalculator.Calculate(
                query.Date,
                professionalWindows.Select(window =>
                    (window.StartTime, window.EndTime)),
                (int)totalDuration,
                business.SlotIntervalMinutes);

            foreach (var localSlot in localSlots)
            {
                var slot = SlotTimeConverter.TryConvert(
                    professionalWindows.Key,
                    localSlot,
                    timezone);

                if (slot is not null && slot.StartsAt > now)
                {
                    slots.Add(slot);
                }
            }
        }

        var response = new SearchAvailableSlotsResponse(
            location.Id,
            query.Date,
            location.Timezone,
            (int)totalDuration,
            slots
                .Distinct()
                .OrderBy(slot => slot.StartsAt)
                .ThenBy(slot => slot.ProfessionalId)
                .ToArray());

        return SearchAvailableSlotsResult.Success(response);
    }

    private static SearchAvailableSlotsResult Failure(
        SearchAvailableSlotsError error)
        => SearchAvailableSlotsResult.Failure(error);
}