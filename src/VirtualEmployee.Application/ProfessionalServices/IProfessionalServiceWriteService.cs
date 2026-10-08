using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Application.ProfessionalServices;

public interface IProfessionalServiceWriteService
{
    Task<Professional?> GetProfessionalByIdAsync(
        Guid professionalId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Service>> GetServicesByIdsAsync(
        IReadOnlyCollection<Guid> serviceIds,
        CancellationToken cancellationToken = default);

    Task ReplaceAsync(
        Guid professionalId,
        IReadOnlyCollection<Guid> serviceIds,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}