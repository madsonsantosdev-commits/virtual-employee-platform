using VirtualEmployee.Application.Services;

namespace VirtualEmployee.Application.ProfessionalServices;

public interface IProfessionalServiceReadService
{
    Task<IReadOnlyList<ServiceResponse>> GetByProfessionalIdAsync(
        Guid professionalId,
        CancellationToken cancellationToken = default);
}