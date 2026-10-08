using VirtualEmployee.Application.Professionals.GetProfessionals;

namespace VirtualEmployee.Application.Professionals;

public interface IProfessionalReadService
{
    Task<IReadOnlyList<ProfessionalResponse>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProfessionalResponse>> GetAllAsync(
        GetProfessionalsQuery query,
        CancellationToken cancellationToken = default);

    Task<ProfessionalResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}