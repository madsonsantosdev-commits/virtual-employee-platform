namespace VirtualEmployee.Application.Businesses.GetBusiness;

public sealed class GetBusinessHandler
{
    private readonly IBusinessReadService _businessReadService;

    public GetBusinessHandler(
        IBusinessReadService businessReadService)
    {
        _businessReadService = businessReadService;
    }

    public Task<BusinessResponse?> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        return _businessReadService.GetCurrentAsync(
            cancellationToken);
    }
}