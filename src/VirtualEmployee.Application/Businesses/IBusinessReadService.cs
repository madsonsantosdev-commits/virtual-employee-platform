namespace VirtualEmployee.Application.Businesses;

public interface IBusinessReadService
{
    Task<BusinessResponse?> GetCurrentAsync(
        CancellationToken cancellationToken = default);
}