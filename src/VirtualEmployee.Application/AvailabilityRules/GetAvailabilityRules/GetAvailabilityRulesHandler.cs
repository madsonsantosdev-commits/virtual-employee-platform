namespace VirtualEmployee.Application.AvailabilityRules.GetAvailabilityRules;

public sealed class GetAvailabilityRulesHandler
{
    private readonly IAvailabilityRuleReadService _readService;
    private readonly IAvailabilityRuleWriteService _writeService;

    public GetAvailabilityRulesHandler(
        IAvailabilityRuleReadService readService,
        IAvailabilityRuleWriteService writeService)
    {
        _readService = readService;
        _writeService = writeService;
    }

    public async Task<IReadOnlyList<AvailabilityRuleResponse>?> HandleAsync(
        Guid locationId,
        Guid professionalId,
        CancellationToken cancellationToken = default)
    {
        var location = await _writeService.GetLocationByIdAsync(
            locationId,
            cancellationToken);

        if (location is null)
        {
            return null;
        }

        var professional =
            await _writeService.GetProfessionalByIdAsync(
                professionalId,
                cancellationToken);

        if (professional is null ||
            professional.BusinessId != location.BusinessId)
        {
            return null;
        }

        var link = await _writeService.GetProfessionalLocationAsync(
            locationId,
            professionalId,
            cancellationToken);

        if (link is null)
        {
            return null;
        }

        return await _readService.GetByProfessionalLocationAsync(
            locationId,
            professionalId,
            cancellationToken);
    }
}