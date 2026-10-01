using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Services;

public sealed class ServiceComponent : ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid ComboServiceId { get; private set; }
    public Guid ComponentServiceId { get; private set; }
    public int SortOrder { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ServiceComponent()
    {
    }

    public ServiceComponent(
        Guid tenantId,
        Guid comboServiceId,
        Guid componentServiceId,
        int sortOrder,
        DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tenant id cannot be empty.",
                nameof(tenantId));
        }

        if (comboServiceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Combo service id cannot be empty.",
                nameof(comboServiceId));
        }

        if (componentServiceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Component service id cannot be empty.",
                nameof(componentServiceId));
        }

        if (comboServiceId == componentServiceId)
        {
            throw new ArgumentException(
                "A combo cannot contain itself.",
                nameof(componentServiceId));
        }

        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sortOrder),
                "Sort order cannot be negative.");
        }
        
        TenantId = tenantId;
        ComboServiceId = comboServiceId;
        ComponentServiceId = componentServiceId;
        SortOrder = sortOrder;
        CreatedAt = createdAt;
    }
    public void UpdateSortOrder(int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sortOrder),
                "Sort order cannot be negative.");
        }

        SortOrder = sortOrder;
    }
}