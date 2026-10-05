namespace VirtualEmployee.Application.Professionals.GetProfessionals;

public sealed class GetProfessionalByIdHandler
{
    private readonly IProfessionalReadService _professionalReadService;

    public GetProfessionalByIdHandler(
        IProfessionalReadService professionalReadService)
    {
        _professionalReadService = professionalReadService;
    }

    public Task<ProfessionalResponse?> HandleAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _professionalReadService.GetByIdAsync(
            id,
            cancellationToken);
    }
}