namespace VirtualEmployee.Domain.Common;

public interface ITenantScoped
{
    Guid TenantId { get; }
}