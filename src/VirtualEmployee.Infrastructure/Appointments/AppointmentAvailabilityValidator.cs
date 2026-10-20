using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Appointments.CreateAppointment;
using VirtualEmployee.Application.AvailabilitySlots.SearchAvailableSlots;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Infrastructure.AvailabilitySlots;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Appointments;

internal sealed class AppointmentAvailabilityValidator
{
    private readonly AppDbContext _dbContext;

    public AppointmentAvailabilityValidator(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<CreateAppointmentError> ValidateAsync(
        CreateAppointmentCommand command,
        AppointmentResourceValidation resources,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var quote = resources.Quote
            ?? throw new InvalidOperationException(
                "Validated resources must contain a quote.");

        return ValidateAsync(
            command.LocationId,
            command.ProfessionalId,
            command.ServiceIds,
            resources.BusinessId,
            resources.Timezone!,
            command.StartsAt,
            quote.TotalDurationMinutes,
            now,
            null,
            cancellationToken);
    }

    public async Task<CreateAppointmentError> ValidateAsync(
        Guid locationId,
        Guid professionalId,
        IReadOnlyList<Guid> serviceIds,
        Guid businessId,
        string timezoneId,
        DateTimeOffset requestedStartsAt,
        int durationMinutes,
        DateTimeOffset now,
        Guid? excludedAppointmentId,
        CancellationToken cancellationToken)
    {
        if (requestedStartsAt <= now || durationMinutes <= 0)
        {
            return CreateAppointmentError.SlotUnavailable;
        }

        TimeZoneInfo timezone;

        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return CreateAppointmentError.InvalidTimezone;
        }
        catch (InvalidTimeZoneException)
        {
            return CreateAppointmentError.InvalidTimezone;
        }

        var localStart = TimeZoneInfo.ConvertTime(
            requestedStartsAt,
            timezone);

        var date = DateOnly.FromDateTime(localStart.DateTime);

        var business = await _dbContext.Businesses
            .AsNoTracking()
            .SingleAsync(
                entity => entity.Id == businessId,
                cancellationToken);

        var readService = new AvailabilitySlotReadService(_dbContext);

        var windows = await readService.GetEligibleWindowsAsync(
            locationId,
            businessId,
            serviceIds,
            date.DayOfWeek,
            professionalId,
            cancellationToken);

        LocalSlot[] localSlots;

        try
        {
            localSlots = SlotCalculator.Calculate(
                    date,
                    windows.Select(window =>
                        (window.StartTime, window.EndTime)),
                    durationMinutes,
                    business.SlotIntervalMinutes)
                .ToArray();
        }
        catch (ArgumentOutOfRangeException)
        {
            return CreateAppointmentError.SlotUnavailable;
        }

        AvailableSlotResponse? selectedSlot = null;

        foreach (var localSlot in localSlots)
        {
            var candidate = SlotTimeConverter.TryConvert(
                professionalId,
                localSlot,
                timezone);

            if (candidate is not null &&
                candidate.StartsAt == requestedStartsAt)
            {
                selectedSlot = candidate;
                break;
            }
        }

        if (selectedSlot is null)
        {
            return CreateAppointmentError.SlotUnavailable;
        }

        var startsAt = selectedSlot.StartsAt;
        var endsAt = selectedSlot.EndsAt;

        var blocks = await readService.GetBlocksAsync(
            locationId,
            startsAt,
            endsAt,
            professionalId,
            cancellationToken);

        if (blocks.Any(block =>
                block.IsActive &&
                block.LocationId == locationId &&
                (block.ProfessionalId is null ||
                 block.ProfessionalId == professionalId) &&
                block.StartsAt < endsAt &&
                startsAt < block.EndsAt))
        {
            return CreateAppointmentError.SlotUnavailable;
        }

        var appointments = _dbContext.Appointments
            .AsNoTracking()
            .Where(entity =>
                entity.ProfessionalId == professionalId &&
                (entity.Status == AppointmentStatus.Pending ||
                 entity.Status == AppointmentStatus.Confirmed ||
                 entity.Status == AppointmentStatus.ConfirmedByClient ||
                 entity.Status == AppointmentStatus.RescheduleRequested) &&
                entity.StartsAt < endsAt &&
                startsAt < entity.EndsAt);

        if (excludedAppointmentId is Guid appointmentId)
        {
            appointments = appointments.Where(
                entity => entity.Id != appointmentId);
        }

        // Considera o profissional em todas as unidades do tenant.
        var occupied = await appointments.AnyAsync(cancellationToken);

        return occupied
            ? CreateAppointmentError.SlotUnavailable
            : CreateAppointmentError.None;
    }
}