using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Application.Services;

public interface IServiceWriteService
{
    Task<bool> BusinessExistsAsync(
        Guid businessId,
        CancellationToken cancellationToken = default);

    Task<Service?> GetByIdAsync(
        Guid serviceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Service>> GetByIdsAsync(
        IReadOnlyCollection<Guid> serviceIds,
        CancellationToken cancellationToken = default);

    void Add(Service service);

    void AddComponents(
        IEnumerable<ServiceComponent> components);

    Task ReplaceComponentsAsync(
        Guid comboServiceId,
        IReadOnlyList<Guid> componentServiceIds,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}