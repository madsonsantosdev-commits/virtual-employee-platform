using VirtualEmployee.Application.Services;

namespace VirtualEmployee.Application.ProfessionalServices.GetProfessionalServices;

public sealed class GetProfessionalServicesHandler
{
    private readonly IProfessionalServiceReadService _readService;
    private readonly IProfessionalServiceWriteService _writeService;

    public GetProfessionalServicesHandler(
        IProfessionalServiceReadService readService,
        IProfessionalServiceWriteService writeService)
    {
        _readService = readService;
        _writeService = writeService;
    }

    public async Task<IReadOnlyList<ServiceResponse>?> HandleAsync(
        Guid professionalId,
        CancellationToken cancellationToken = default)
    {
        var professional = await _writeService.GetProfessionalByIdAsync(
            professionalId,
            cancellationToken);

        if (professional is null)
        {
            return null;
        }

        return await _readService.GetByProfessionalIdAsync(
            professionalId,
            cancellationToken);
    }
}