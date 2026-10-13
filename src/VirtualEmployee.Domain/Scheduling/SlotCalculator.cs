namespace VirtualEmployee.Domain.Scheduling;

public static class SlotCalculator
{
    public static IReadOnlyList<LocalSlot> Calculate(
        DateOnly date,
        IEnumerable<(TimeOnly StartTime, TimeOnly EndTime)> windows,
        int durationMinutes,
        int slotIntervalMinutes)
    {
        ArgumentNullException.ThrowIfNull(windows);

        if (durationMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationMinutes));
        }

        if (slotIntervalMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotIntervalMinutes));
        }

        var orderedWindows = windows
            .OrderBy(window => window.StartTime)
            .ThenBy(window => window.EndTime)
            .ToArray();

        var mergedWindows =
            new List<(TimeSpan Start, TimeSpan End)>();

        foreach (var window in orderedWindows)
        {
            if (window.StartTime >= window.EndTime)
            {
                throw new ArgumentException(
                    "Each window must start before it ends.",
                    nameof(windows));
            }

            var start = window.StartTime.ToTimeSpan();
            var end = window.EndTime.ToTimeSpan();

            if (mergedWindows.Count == 0 ||
                start > mergedWindows[^1].End)
            {
                mergedWindows.Add((start, end));
                continue;
            }

            var previous = mergedWindows[^1];

            mergedWindows[^1] = (
                previous.Start,
                end > previous.End ? end : previous.End);
        }

        var duration = TimeSpan.FromMinutes(durationMinutes);
        var interval = TimeSpan.FromMinutes(slotIntervalMinutes);
        var midnight = date.ToDateTime(
            TimeOnly.MinValue,
            DateTimeKind.Unspecified);

        var slots = new List<LocalSlot>();

        foreach (var window in mergedWindows)
        {
            for (var start = window.Start;
                 start + duration <= window.End;
                 start += interval)
            {
                slots.Add(
                    new LocalSlot(
                        midnight.Add(start),
                        midnight.Add(start + duration)));
            }
        }

        return slots;
    }
}