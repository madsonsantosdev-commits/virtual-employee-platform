using VirtualEmployee.Application.Locations;

namespace VirtualEmployee.Application.ProfessionalLocations;

public interface IProfessionalLocationReadService
{
    Task<IReadOnlyList<LocationResponse>> GetByProfessionalIdAsync(
        Guid professionalId,
        CancellationToken cancellationToken = default);
}