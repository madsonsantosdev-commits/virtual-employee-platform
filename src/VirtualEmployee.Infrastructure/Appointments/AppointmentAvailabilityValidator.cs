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

    public async Task<CreateAppointmentError> ValidateAsync(
        CreateAppointmentCommand command,
        AppointmentResourceValidation resources,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (command.StartsAt <= now)
        {
            return CreateAppointmentError.SlotUnavailable;
        }

        var quote = resources.Quote
            ?? throw new InvalidOperationException(
                "Validated resources must contain a quote.");

        TimeZoneInfo timezone;

        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(
                resources.Timezone!);
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
            command.StartsAt,
            timezone);

        var date = DateOnly.FromDateTime(localStart.DateTime);

        var business = await _dbContext.Businesses
            .AsNoTracking()
            .SingleAsync(
                entity => entity.Id == resources.BusinessId,
                cancellationToken);

        var readService = new AvailabilitySlotReadService(_dbContext);

        var windows = await readService.GetEligibleWindowsAsync(
            command.LocationId,
            resources.BusinessId,
            command.ServiceIds,
            date.DayOfWeek,
            command.ProfessionalId,
            cancellationToken);

        LocalSlot[] localSlots;

        try
        {
            localSlots = SlotCalculator.Calculate(
                    date,
                    windows.Select(window =>
                        (window.StartTime, window.EndTime)),
                    quote.TotalDurationMinutes,
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
                command.ProfessionalId,
                localSlot,
                timezone);

            if (candidate is not null &&
                candidate.StartsAt == command.StartsAt)
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
            command.LocationId,
            startsAt,
            endsAt,
            command.ProfessionalId,
            cancellationToken);

        if (blocks.Any(block =>
                block.IsActive &&
                block.LocationId == command.LocationId &&
                (block.ProfessionalId is null ||
                 block.ProfessionalId == command.ProfessionalId) &&
                block.StartsAt < endsAt &&
                startsAt < block.EndsAt))
        {
            return CreateAppointmentError.SlotUnavailable;
        }

        // O profissional ocupa agenda globalmente dentro do tenant,
        // inclusive quando o outro atendimento ocorre em outra unidade.
        var occupied = await _dbContext.Appointments
            .AsNoTracking()
            .AnyAsync(
                entity =>
                    entity.ProfessionalId == command.ProfessionalId &&
                    (entity.Status == AppointmentStatus.Pending ||
                     entity.Status == AppointmentStatus.Confirmed ||
                     entity.Status == AppointmentStatus.ConfirmedByClient ||
                     entity.Status == AppointmentStatus.RescheduleRequested) &&
                    entity.StartsAt < endsAt &&
                    startsAt < entity.EndsAt,
                cancellationToken);

        return occupied
            ? CreateAppointmentError.SlotUnavailable
            : CreateAppointmentError.None;
    }
}