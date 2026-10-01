using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Services;

public sealed class Service : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BusinessId { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public ServiceType ServiceType { get; private set; }
    public decimal Price { get; private set; }
    public int DurationMinutes { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Service()
    {
    }

    public Service(
        Guid id,
        Guid tenantId,
        Guid businessId,
        string name,
        ServiceType serviceType,
        decimal price,
        int durationMinutes,
        DateTimeOffset createdAt)
    {
        ValidateRequiredFields(
            id,
            tenantId,
            businessId,
            serviceType);

        ApplyOperationalData(
            name,
            price,
            durationMinutes);

        Id = id;
        TenantId = tenantId;
        BusinessId = businessId;
        ServiceType = serviceType;

        IsActive = true;

        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public void Update(
        string name,
        decimal price,
        int durationMinutes,
        bool isActive,
        DateTimeOffset updatedAt)
    {
        ApplyOperationalData(
            name,
            price,
            durationMinutes);

        IsActive = isActive;
        UpdatedAt = updatedAt;
    }

    private static void ValidateRequiredFields(
        Guid id,
        Guid tenantId,
        Guid businessId,
        ServiceType serviceType)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Service id cannot be empty.",
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

        if (!Enum.IsDefined(serviceType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(serviceType),
                "Service type is invalid.");
        }
    }

    private void ApplyOperationalData(
        string name,
        decimal price,
        int durationMinutes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Service name cannot be empty.",
                nameof(name));
        }

        if (price < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                "Service price cannot be negative.");
        }

        if (durationMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationMinutes),
                "Service duration must be greater than zero.");
        }

        Name = name.Trim();
        Price = price;
        DurationMinutes = durationMinutes;
    }
}