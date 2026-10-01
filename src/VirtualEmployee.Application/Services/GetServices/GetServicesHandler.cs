namespace VirtualEmployee.Application.Services.GetServices;

public sealed class GetServicesHandler
{
    private readonly IServiceReadService _serviceReadService;

    public GetServicesHandler(
        IServiceReadService serviceReadService)
    {
        _serviceReadService = serviceReadService;
    }

    public Task<IReadOnlyList<ServiceResponse>> HandleAsync(
        bool? active = null,
        CancellationToken cancellationToken = default)
    {
        return _serviceReadService.GetAllAsync(
            active,
            cancellationToken);
    }
}