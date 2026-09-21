using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Businesses;

public sealed class Business : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    private Business()
    {
    }

    public Business(
        Guid id,
        Guid tenantId,
        string name)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Business id cannot be empty.",
                nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tenant id cannot be empty.",
                nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Business name cannot be empty.",
                nameof(name));
        }

        Id = id;
        TenantId = tenantId;
        Name = name.Trim();
    }
}