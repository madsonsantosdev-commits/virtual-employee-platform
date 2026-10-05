namespace VirtualEmployee.Application.Professionals;

public interface IProfessionalReadService
{
    Task<IReadOnlyList<ProfessionalResponse>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<ProfessionalResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}