namespace VirtualEmployee.Domain.Scheduling;

public sealed record LocalSlot(
    DateTime StartsAt,
    DateTime EndsAt);