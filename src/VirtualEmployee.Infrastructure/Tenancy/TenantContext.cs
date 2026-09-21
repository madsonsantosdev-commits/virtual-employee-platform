using VirtualEmployee.Application.Common.Tenancy;

namespace VirtualEmployee.Infrastructure.Tenancy;

public sealed class TenantContext : ITenantContext
{
    public Guid TenantId { get; }

    public TenantContext(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tenant id cannot be empty.",
                nameof(tenantId));
        }

        TenantId = tenantId;
    }
}
