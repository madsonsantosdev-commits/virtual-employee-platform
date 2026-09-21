using VirtualEmployee.Application.Common.Tenancy;

namespace VirtualEmployee.Infrastructure.Tenancy;

public sealed class TenantContext :
    ITenantContext,
    ITenantContextInitializer
{
    private Guid? _tenantId;

    public Guid TenantId =>
        _tenantId ?? throw new InvalidOperationException(
            "Tenant context has not been initialized.");

    public void Initialize(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tenant id cannot be empty.",
                nameof(tenantId));
        }

        if (_tenantId.HasValue)
        {
            throw new InvalidOperationException(
                "Tenant context has already been initialized.");
        }

        _tenantId = tenantId;
    }
}