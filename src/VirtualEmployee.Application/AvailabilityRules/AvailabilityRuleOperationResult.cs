namespace VirtualEmployee.Application.AvailabilityRules;

public sealed record AvailabilityRuleOperationResult(
    AvailabilityRuleOperationError Error)
{
    public bool IsSuccess =>
        Error == AvailabilityRuleOperationError.None;

    public static AvailabilityRuleOperationResult Success()
    {
        return new(AvailabilityRuleOperationError.None);
    }

    public static AvailabilityRuleOperationResult Failure(
        AvailabilityRuleOperationError error)
    {
        return new(error);
    }
}