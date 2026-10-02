using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Services;

public sealed class LocationService : ITenantScoped
{
    private LocationService()
    {
    }

    public LocationService(
        Guid tenantId,
        Guid locationId,
        Guid serviceId,
        DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "TenantId cannot be empty.",
                nameof(tenantId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException(
                "LocationId cannot be empty.",
                nameof(locationId));
        }

        if (serviceId == Guid.Empty)
        {
            throw new ArgumentException(
                "ServiceId cannot be empty.",
                nameof(serviceId));
        }

        TenantId = tenantId;
        LocationId = locationId;
        ServiceId = serviceId;
        CreatedAt = createdAt;
    }

    public Guid TenantId { get; private set; }

    public Guid LocationId { get; private set; }

    public Guid ServiceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}