namespace VirtualEmployee.Application.Professionals.UpdateProfessional;

public sealed class UpdateProfessionalHandler
{
    private readonly IProfessionalWriteService _professionalWriteService;

    public UpdateProfessionalHandler(
        IProfessionalWriteService professionalWriteService)
    {
        _professionalWriteService = professionalWriteService;
    }

    public async Task<ProfessionalResponse?> HandleAsync(
        UpdateProfessionalCommand command,
        CancellationToken cancellationToken = default)
    {
        var professional =
            await _professionalWriteService.GetByIdAsync(
                command.ProfessionalId,
                cancellationToken);

        if (professional is null)
        {
            return null;
        }

        professional.Update(
            command.Name,
            command.IsActive,
            DateTimeOffset.UtcNow);

        return new ProfessionalResponse(
            professional.Id,
            professional.BusinessId,
            professional.Name,
            professional.IsActive);
    }
}