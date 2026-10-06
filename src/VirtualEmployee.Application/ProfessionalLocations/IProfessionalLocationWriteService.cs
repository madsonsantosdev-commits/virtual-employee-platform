using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Professionals;

namespace VirtualEmployee.Application.ProfessionalLocations;

public interface IProfessionalLocationWriteService
{
    Task<Professional?> GetProfessionalByIdAsync(
        Guid professionalId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Location>> GetLocationsByIdsAsync(
        IReadOnlyCollection<Guid> locationIds,
        CancellationToken cancellationToken = default);

    Task ReplaceAsync(
        Guid professionalId,
        IReadOnlyCollection<Guid> locationIds,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}