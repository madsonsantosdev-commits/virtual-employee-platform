namespace VirtualEmployee.Application.AvailabilityRules;

public sealed record AvailabilityRuleResponse(
    Guid Id,
    int DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);