using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Professionals;

public sealed class Professional : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BusinessId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Professional()
    {
    }

    public Professional(
        Guid id,
        Guid tenantId,
        Guid businessId,
        string name,
        DateTimeOffset createdAt)
    {
        ValidateRequiredFields(
            id,
            tenantId,
            businessId);

        ApplyOperationalData(name);

        Id = id;
        TenantId = tenantId;
        BusinessId = businessId;

        IsActive = true;

        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public void Update(
        string name,
        bool isActive,
        DateTimeOffset updatedAt)
    {
        ApplyOperationalData(name);

        IsActive = isActive;
        UpdatedAt = updatedAt;
    }

    private static void ValidateRequiredFields(
        Guid id,
        Guid tenantId,
        Guid businessId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Professional id cannot be empty.",
                nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tenant id cannot be empty.",
                nameof(tenantId));
        }

        if (businessId == Guid.Empty)
        {
            throw new ArgumentException(
                "Business id cannot be empty.",
                nameof(businessId));
        }
    }

    private void ApplyOperationalData(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Professional name cannot be empty.",
                nameof(name));
        }

        Name = name.Trim();
    }
}