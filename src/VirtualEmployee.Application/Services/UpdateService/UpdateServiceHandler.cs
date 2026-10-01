using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Application.Services.UpdateService;

public sealed class UpdateServiceHandler
{
    private readonly IServiceWriteService _serviceWriteService;

    public UpdateServiceHandler(
        IServiceWriteService serviceWriteService)
    {
        _serviceWriteService = serviceWriteService;
    }

    public async Task<ServiceOperationResult> HandleAsync(
        UpdateServiceCommand command,
        CancellationToken cancellationToken = default)
    {
        var service = await _serviceWriteService.GetByIdAsync(
            command.ServiceId,
            cancellationToken);

        if (service is null)
        {
            return ServiceOperationResult.Failure(
                ServiceOperationError.ServiceNotFound);
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

        if (service.ServiceType == ServiceType.Single &&
            componentIds.Length > 0)
        {
            return ServiceOperationResult.Failure(
                ServiceOperationError.InvalidCombo);
        }

        if (service.ServiceType == ServiceType.Combo &&
            componentIds.Length == 0)
        {
            return ServiceOperationResult.Failure(
                ServiceOperationError.InvalidCombo);
        }

        if (service.ServiceType == ServiceType.Combo)
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
                    component.BusinessId != service.BusinessId))
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

        service.Update(
            command.Name,
            command.Price,
            command.DurationMinutes,
            command.IsActive,
            now);

        if (service.ServiceType == ServiceType.Combo)
        {
            await _serviceWriteService.ReplaceComponentsAsync(
                service.Id,
                componentIds,
                now,
                cancellationToken);
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