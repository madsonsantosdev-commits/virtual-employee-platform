namespace VirtualEmployee.Application.ProfessionalLocations.ReplaceProfessionalLocations;

public sealed class ReplaceProfessionalLocationsHandler
{
    private readonly IProfessionalLocationWriteService _writeService;

    public ReplaceProfessionalLocationsHandler(
        IProfessionalLocationWriteService writeService)
    {
        _writeService = writeService;
    }

    public async Task<ProfessionalLocationOperationResult> HandleAsync(
        ReplaceProfessionalLocationsCommand command,
        CancellationToken cancellationToken = default)
    {
        var professional = await _writeService.GetProfessionalByIdAsync(
            command.ProfessionalId,
            cancellationToken);

        if (professional is null)
        {
            return ProfessionalLocationOperationResult.Failure(
                ProfessionalLocationOperationError.ProfessionalNotFound);
        }

        if (command.LocationIds.Count !=
            command.LocationIds.Distinct().Count())
        {
            return ProfessionalLocationOperationResult.Failure(
                ProfessionalLocationOperationError.DuplicateLocation);
        }

        var locations = await _writeService.GetLocationsByIdsAsync(
            command.LocationIds,
            cancellationToken);

        if (locations.Count != command.LocationIds.Count)
        {
            return ProfessionalLocationOperationResult.Failure(
                ProfessionalLocationOperationError.LocationNotFound);
        }

        if (locations.Any(location =>
                location.BusinessId != professional.BusinessId))
        {
            return ProfessionalLocationOperationResult.Failure(
                ProfessionalLocationOperationError.LocationFromAnotherBusiness);
        }

        await _writeService.ReplaceAsync(
            command.ProfessionalId,
            command.LocationIds,
            DateTimeOffset.UtcNow,
            cancellationToken);

        await _writeService.SaveChangesAsync(
            cancellationToken);

        return ProfessionalLocationOperationResult.Success();
    }
}