using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Application.Services.CreateService;

public sealed class CreateServiceHandler
{
    private readonly ITenantContext _tenantContext;
    private readonly IServiceWriteService _serviceWriteService;

    public CreateServiceHandler(
        ITenantContext tenantContext,
        IServiceWriteService serviceWriteService)
    {
        _tenantContext = tenantContext;
        _serviceWriteService = serviceWriteService;
    }

    public async Task<ServiceOperationResult> HandleAsync(
        CreateServiceCommand command,
        CancellationToken cancellationToken = default)
    {
        var businessExists =
            await _serviceWriteService.BusinessExistsAsync(
                command.BusinessId,
                cancellationToken);

        if (!businessExists)
        {
            return ServiceOperationResult.Failure(
                ServiceOperationError.BusinessNotFound);
        }

        var componentIds =
            command.ComponentServiceIds?.ToArray()
            ?? [];

        if (componentIds.Length !=
            componentIds.Distinct().Count())
        {
            return ServiceOperationResult.Failure(
                ServiceOperationError.ComboComponentInvalid);
        }

        if (command.ServiceType == ServiceType.Single &&
            componentIds.Length > 0)
        {
            return ServiceOperationResult.Failure(
                ServiceOperationError.InvalidCombo);
        }

        if (command.ServiceType == ServiceType.Combo &&
            componentIds.Length == 0)
        {
            return ServiceOperationResult.Failure(
                ServiceOperationError.InvalidCombo);
        }

        if (command.ServiceType == ServiceType.Combo)
        {
            var components =
                await _serviceWriteService.GetByIdsAsync(
                    componentIds,
                    cancellationToken);

            if (components.Count != componentIds.Length)
            {
                return ServiceOperationResult.Failure(
                    ServiceOperationError.ComboComponentInvalid);
            }

            if (components.Any(
                component =>
                    component.BusinessId != command.BusinessId))
            {
                return ServiceOperationResult.Failure(
                    ServiceOperationError.ComboComponentInvalid);
            }

            if (components.Any(
                component =>
                    component.ServiceType == ServiceType.Combo))
            {
                return ServiceOperationResult.Failure(
                    ServiceOperationError.ComboNestingNotAllowed);
            }
        }

        var now = DateTimeOffset.UtcNow;

        var service = new Service(
            Guid.NewGuid(),
            _tenantContext.TenantId,
            command.BusinessId,
            command.Name,
            command.ServiceType,
            command.Price,
            command.DurationMinutes,
            now);

        _serviceWriteService.Add(service);

        if (service.ServiceType == ServiceType.Combo)
        {
            var components = componentIds.Select(
                (componentId, index) =>
                    new ServiceComponent(
                        _tenantContext.TenantId,
                        service.Id,
                        componentId,
                        index,
                        now));

            _serviceWriteService.AddComponents(components);
        }

        await _serviceWriteService.SaveChangesAsync(
            cancellationToken);

        return ServiceOperationResult.Success(
            new ServiceResponse(
                service.Id,
                service.BusinessId,
                service.Name,
                service.ServiceType,
                service.Price,
                service.DurationMinutes,
                service.IsActive,
                componentIds));
    }
}