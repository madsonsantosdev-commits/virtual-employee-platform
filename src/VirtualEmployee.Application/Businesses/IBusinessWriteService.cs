using VirtualEmployee.Domain.Businesses;

namespace VirtualEmployee.Application.Businesses;

public interface IBusinessWriteService
{
    Task<Business?> GetCurrentAsync(
        CancellationToken cancellationToken = default);

    Task<bool> IsBusinessTypeActiveAsync(
        Guid businessTypeId,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}