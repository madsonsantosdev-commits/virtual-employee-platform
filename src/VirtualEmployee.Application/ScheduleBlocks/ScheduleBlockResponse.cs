namespace VirtualEmployee.Application.ScheduleBlocks;

public sealed record ScheduleBlockResponse(
    Guid Id,
    Guid LocationId,
    Guid? ProfessionalId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);