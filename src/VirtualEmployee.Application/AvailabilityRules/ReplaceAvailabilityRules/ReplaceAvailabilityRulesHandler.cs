namespace VirtualEmployee.Application.AvailabilityRules.ReplaceAvailabilityRules;

public sealed class ReplaceAvailabilityRulesHandler
{
    private readonly IAvailabilityRuleWriteService _writeService;

    public ReplaceAvailabilityRulesHandler(
        IAvailabilityRuleWriteService writeService)
    {
        _writeService = writeService;
    }

    public async Task<AvailabilityRuleOperationResult> HandleAsync(
        ReplaceAvailabilityRulesCommand command,
        CancellationToken cancellationToken = default)
    {
        var location = await _writeService.GetLocationByIdAsync(
            command.LocationId,
            cancellationToken);

        if (location is null)
        {
            return Failure(
                AvailabilityRuleOperationError.LocationNotFound);
        }

        var professional =
            await _writeService.GetProfessionalByIdAsync(
                command.ProfessionalId,
                cancellationToken);

        if (professional is null)
        {
            return Failure(
                AvailabilityRuleOperationError.ProfessionalNotFound);
        }

        if (professional.BusinessId != location.BusinessId)
        {
            return Failure(
                AvailabilityRuleOperationError
                    .ProfessionalFromAnotherBusiness);
        }

        var link = await _writeService.GetProfessionalLocationAsync(
            command.LocationId,
            command.ProfessionalId,
            cancellationToken);

        if (link is null)
        {
            return Failure(
                AvailabilityRuleOperationError.ProfessionalLocationNotFound);
        }

        if (!location.IsActive)
        {
            return Failure(
                AvailabilityRuleOperationError.InactiveLocation);
        }

        if (!professional.IsActive)
        {
            return Failure(
                AvailabilityRuleOperationError.InactiveProfessional);
        }

        if (!link.IsActive)
        {
            return Failure(
                AvailabilityRuleOperationError.InactiveProfessionalLocation);
        }

        foreach (var rule in command.Rules)
        {
            if (!Enum.IsDefined(rule.DayOfWeek))
            {
                return Failure(
                    AvailabilityRuleOperationError.InvalidDayOfWeek);
            }

            if (rule.StartTime >= rule.EndTime)
            {
                return Failure(
                    AvailabilityRuleOperationError.InvalidTimeWindow);
            }
        }

        var orderedRules = command.Rules
            .OrderBy(rule => rule.DayOfWeek)
            .ThenBy(rule => rule.StartTime)
            .ThenBy(rule => rule.EndTime)
            .ToArray();

        for (var index = 1; index < orderedRules.Length; index++)
        {
            var previous = orderedRules[index - 1];
            var current = orderedRules[index];

            if (previous.DayOfWeek == current.DayOfWeek &&
                current.StartTime < previous.EndTime)
            {
                return Failure(
                    AvailabilityRuleOperationError.OverlappingRules);
            }
        }

        await _writeService.ReplaceAsync(
            command.LocationId,
            command.ProfessionalId,
            orderedRules,
            DateTimeOffset.UtcNow,
            cancellationToken);

        await _writeService.SaveChangesAsync(
            cancellationToken);

        return AvailabilityRuleOperationResult.Success();
    }

    private static AvailabilityRuleOperationResult Failure(
        AvailabilityRuleOperationError error)
    {
        return AvailabilityRuleOperationResult.Failure(error);
    }
}