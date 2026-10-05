using VirtualEmployee.Domain.Professionals;

namespace VirtualEmployee.Application.Professionals;

public interface IProfessionalWriteService
{
    Task<bool> BusinessExistsAsync(
        Guid businessId,
        CancellationToken cancellationToken = default);

    Task<Professional?> GetByIdAsync(
        Guid professionalId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Professional professional,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}