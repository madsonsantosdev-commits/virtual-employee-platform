using VirtualEmployee.Domain.Scheduling;

namespace VirtualEmployee.Application.AvailabilitySlots.SearchAvailableSlots;

public static class SlotTimeConverter
{
    public static AvailableSlotResponse? TryConvert(
        Guid professionalId,
        LocalSlot localSlot,
        TimeZoneInfo timezone)
    {
        ArgumentNullException.ThrowIfNull(localSlot);
        ArgumentNullException.ThrowIfNull(timezone);

        if (professionalId == Guid.Empty)
        {
            throw new ArgumentException(
                "Professional id cannot be empty.",
                nameof(professionalId));
        }

        if (localSlot.StartsAt.Kind != DateTimeKind.Unspecified ||
            localSlot.EndsAt.Kind != DateTimeKind.Unspecified)
        {
            throw new ArgumentException(
                "Slot times must represent local unspecified times.",
                nameof(localSlot));
        }

        if (localSlot.StartsAt >= localSlot.EndsAt)
        {
            throw new ArgumentException(
                "Slot must start before it ends.",
                nameof(localSlot));
        }

        if (timezone.IsInvalidTime(localSlot.StartsAt) ||
            timezone.IsInvalidTime(localSlot.EndsAt) ||
            timezone.IsAmbiguousTime(localSlot.StartsAt) ||
            timezone.IsAmbiguousTime(localSlot.EndsAt))
        {
            return null;
        }

        var startsAt = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(
                localSlot.StartsAt,
                timezone));

        var endsAt = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(
                localSlot.EndsAt,
                timezone));

        var expectedDuration =
            localSlot.EndsAt - localSlot.StartsAt;

        if (endsAt - startsAt != expectedDuration)
        {
            return null;
        }

        return new AvailableSlotResponse(
            professionalId,
            startsAt,
            endsAt);
    }
}
