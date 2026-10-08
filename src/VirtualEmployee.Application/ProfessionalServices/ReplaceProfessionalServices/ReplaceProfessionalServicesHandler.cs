namespace VirtualEmployee.Application.ProfessionalServices.ReplaceProfessionalServices;

public sealed class ReplaceProfessionalServicesHandler
{
    private readonly IProfessionalServiceWriteService _writeService;

    public ReplaceProfessionalServicesHandler(
        IProfessionalServiceWriteService writeService)
    {
        _writeService = writeService;
    }

    public async Task<ProfessionalServiceOperationResult> HandleAsync(
        ReplaceProfessionalServicesCommand command,
        CancellationToken cancellationToken = default)
    {
        var professional = await _writeService.GetProfessionalByIdAsync(
            command.ProfessionalId,
            cancellationToken);

        if (professional is null)
        {
            return ProfessionalServiceOperationResult.Failure(
                ProfessionalServiceOperationError.ProfessionalNotFound);
        }

        if (command.ServiceIds.Count !=
            command.ServiceIds.Distinct().Count())
        {
            return ProfessionalServiceOperationResult.Failure(
                ProfessionalServiceOperationError.DuplicateService);
        }

        var services = await _writeService.GetServicesByIdsAsync(
            command.ServiceIds,
            cancellationToken);

        if (services.Count != command.ServiceIds.Count)
        {
            return ProfessionalServiceOperationResult.Failure(
                ProfessionalServiceOperationError.ServiceNotFound);
        }

        if (services.Any(service =>
                service.BusinessId != professional.BusinessId))
        {
            return ProfessionalServiceOperationResult.Failure(
                ProfessionalServiceOperationError.ServiceFromAnotherBusiness);
        }

        await _writeService.ReplaceAsync(
            command.ProfessionalId,
            command.ServiceIds,
            DateTimeOffset.UtcNow,
            cancellationToken);

        await _writeService.SaveChangesAsync(
            cancellationToken);

        return ProfessionalServiceOperationResult.Success();
    }
}