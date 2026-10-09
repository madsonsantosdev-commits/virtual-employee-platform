namespace VirtualEmployee.Application.AvailabilityRules;

public sealed record AvailabilityRuleInput(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);