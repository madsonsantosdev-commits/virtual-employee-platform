namespace VirtualEmployee.Application.AvailabilityRules.ReplaceAvailabilityRules;

public sealed record ReplaceAvailabilityRulesCommand(
    Guid LocationId,
    Guid ProfessionalId,
    IReadOnlyList<AvailabilityRuleInput> Rules);