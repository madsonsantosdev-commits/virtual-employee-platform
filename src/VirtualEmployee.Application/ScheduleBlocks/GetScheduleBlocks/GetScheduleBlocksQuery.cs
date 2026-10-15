namespace VirtualEmployee.Application.ScheduleBlocks.GetScheduleBlocks;

public sealed record GetScheduleBlocksQuery(
    Guid LocationId,
    DateTimeOffset From,
    DateTimeOffset To,
    Guid? ProfessionalId = null);