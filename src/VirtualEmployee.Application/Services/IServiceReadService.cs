namespace VirtualEmployee.Application.Services;

public interface IServiceReadService
{
    Task<IReadOnlyList<ServiceResponse>> GetAllAsync(
        bool? active = null,
        CancellationToken cancellationToken = default);

    Task<ServiceResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}