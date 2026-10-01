namespace VirtualEmployee.Application.Services.GetServices;

public sealed class GetServiceByIdHandler
{
    private readonly IServiceReadService _serviceReadService;

    public GetServiceByIdHandler(
        IServiceReadService serviceReadService)
    {
        _serviceReadService = serviceReadService;
    }

    public Task<ServiceResponse?> HandleAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _serviceReadService.GetByIdAsync(
            id,
            cancellationToken);
    }
}