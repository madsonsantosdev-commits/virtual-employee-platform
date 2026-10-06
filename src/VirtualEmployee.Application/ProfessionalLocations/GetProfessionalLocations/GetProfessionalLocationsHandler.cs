using VirtualEmployee.Application.Locations;

namespace VirtualEmployee.Application.ProfessionalLocations.GetProfessionalLocations;

public sealed class GetProfessionalLocationsHandler
{
    private readonly IProfessionalLocationReadService _readService;
    private readonly IProfessionalLocationWriteService _writeService;

    public GetProfessionalLocationsHandler(
        IProfessionalLocationReadService readService,
        IProfessionalLocationWriteService writeService)
    {
        _readService = readService;
        _writeService = writeService;
    }

    public async Task<IReadOnlyList<LocationResponse>?> HandleAsync(
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