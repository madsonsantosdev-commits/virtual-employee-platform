namespace VirtualEmployee.Application.Common.Tenancy;

public interface ITenantContext
{
    Guid TenantId { get; }
}