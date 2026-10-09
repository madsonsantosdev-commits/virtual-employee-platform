namespace VirtualEmployee.Application.AvailabilityRules;

public interface IAvailabilityRuleReadService
{
    Task<IReadOnlyList<AvailabilityRuleResponse>> GetByProfessionalLocationAsync(
        Guid locationId,
        Guid professionalId,
        CancellationToken cancellationToken = default);
}