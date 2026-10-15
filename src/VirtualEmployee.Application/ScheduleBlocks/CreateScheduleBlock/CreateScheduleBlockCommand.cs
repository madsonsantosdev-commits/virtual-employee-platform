namespace VirtualEmployee.Application.ScheduleBlocks.CreateScheduleBlock;

public sealed record CreateScheduleBlockCommand(
    Guid LocationId,
    Guid? ProfessionalId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string IdempotencyKey);