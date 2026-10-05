namespace VirtualEmployee.Application.Professionals.GetProfessionals;

public sealed class GetProfessionalsHandler
{
    private readonly IProfessionalReadService _professionalReadService;

    public GetProfessionalsHandler(
        IProfessionalReadService professionalReadService)
    {
        _professionalReadService = professionalReadService;
    }

    public Task<IReadOnlyList<ProfessionalResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        return _professionalReadService.GetAllAsync(cancellationToken);
    }
}