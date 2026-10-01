using VirtualEmployee.Application.Services;
using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Application.Tests.Services;

internal sealed class FakeServiceWriteService : IServiceWriteService
{
    private readonly Dictionary<Guid, Service> _services = [];

    public bool BusinessExists { get; set; } = true;

    public Service? ServiceToReturn { get; set; }

    public Service? AddedService { get; private set; }

    public List<ServiceComponent> AddedComponents { get; } = [];

    public Guid? ReplacedComboServiceId { get; private set; }

    public IReadOnlyList<Guid>? ReplacedComponentServiceIds { get; private set; }

    public int SaveChangesCallCount { get; private set; }

    public void AddExistingService(Service service)
    {
        _services[service.Id] = service;
    }

    public Task<bool> BusinessExistsAsync(
        Guid businessId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(BusinessExists);
    }

    public Task<Service?> GetByIdAsync(
        Guid serviceId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ServiceToReturn);
    }

    public Task<IReadOnlyList<Service>> GetByIdsAsync(
        IReadOnlyCollection<Guid> serviceIds,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Service> result = serviceIds
            .Where(_services.ContainsKey)
            .Select(id => _services[id])
            .ToArray();

        return Task.FromResult(result);
    }

    public void Add(Service service)
    {
        AddedService = service;
        _services[service.Id] = service;
    }

    public void AddComponents(
        IEnumerable<ServiceComponent> components)
    {
        AddedComponents.AddRange(components);
    }

    public Task ReplaceComponentsAsync(
        Guid comboServiceId,
        IReadOnlyList<Guid> componentServiceIds,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        ReplacedComboServiceId = comboServiceId;
        ReplacedComponentServiceIds =
            componentServiceIds.ToArray();

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }
}