using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Professionals;

namespace VirtualEmployee.Application.AvailabilityRules;

public interface IAvailabilityRuleWriteService
{
    Task<Location?> GetLocationByIdAsync(
        Guid locationId,
        CancellationToken cancellationToken = default);

    Task<Professional?> GetProfessionalByIdAsync(
        Guid professionalId,
        CancellationToken cancellationToken = default);

    Task<ProfessionalLocation?> GetProfessionalLocationAsync(
        Guid locationId,
        Guid professionalId,
        CancellationToken cancellationToken = default);

    Task ReplaceAsync(
        Guid locationId,
        Guid professionalId,
        IReadOnlyList<AvailabilityRuleInput> rules,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}