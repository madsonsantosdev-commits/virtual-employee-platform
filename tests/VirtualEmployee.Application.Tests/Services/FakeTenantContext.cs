using VirtualEmployee.Application.Common.Tenancy;

namespace VirtualEmployee.Application.Tests.Services;

internal sealed class FakeTenantContext : ITenantContext
{
    public FakeTenantContext(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "TenantId cannot be empty.",
                nameof(tenantId));
        }

        TenantId = tenantId;
    }

    public Guid TenantId { get; }

    public bool IsInitialized => true;
}