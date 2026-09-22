namespace VirtualEmployee.Application.Common.Tenancy;

public interface ITenantContextInitializer
{
    void Initialize(Guid tenantId);
}