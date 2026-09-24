using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Businesses;

public sealed class Business : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BusinessTypeId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int SlotIntervalMinutes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Business()
    {
    }

    public Business(
        Guid id,
        Guid tenantId,
        Guid businessTypeId,
        string name,
        DateTimeOffset createdAt,
        int slotIntervalMinutes = 15)
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

        if (businessTypeId == Guid.Empty)
        {
            throw new ArgumentException(
                "Business type id cannot be empty.",
                nameof(businessTypeId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Business name cannot be empty.",
                nameof(name));
        }

        if (slotIntervalMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slotIntervalMinutes),
                "Slot interval must be greater than zero.");
        }

        Id = id;
        TenantId = tenantId;
        BusinessTypeId = businessTypeId;
        Name = name.Trim();
        SlotIntervalMinutes = slotIntervalMinutes;
        IsActive = true;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }
}