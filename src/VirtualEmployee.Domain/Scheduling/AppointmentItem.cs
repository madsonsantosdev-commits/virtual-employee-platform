using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Scheduling;

public sealed class AppointmentItem : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public Guid ServiceId { get; private set; }

    public string ServiceNameSnapshot { get; private set; } = string.Empty;
    public decimal PriceSnapshot { get; private set; }
    public int DurationMinutesSnapshot { get; private set; }

    private AppointmentItem()
    {
    }

    public AppointmentItem(
        Guid id,
        Guid tenantId,
        Guid appointmentId,
        Guid serviceId,
        string serviceNameSnapshot,
        decimal priceSnapshot,
        int durationMinutesSnapshot)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Appointment item id cannot be empty.",
                nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tenant id cannot be empty.",
                nameof(tenantId));
        }

        if (appointmentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Appointment id cannot be empty.",
                nameof(appointmentId));
        }

        if (serviceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Service id cannot be empty.",
                nameof(serviceId));
        }

        if (string.IsNullOrWhiteSpace(serviceNameSnapshot))
        {
            throw new ArgumentException(
                "Service name snapshot cannot be empty.",
                nameof(serviceNameSnapshot));
        }

        if (priceSnapshot < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(priceSnapshot),
                "Price snapshot cannot be negative.");
        }

        if (durationMinutesSnapshot <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationMinutesSnapshot),
                "Duration snapshot must be greater than zero.");
        }

        Id = id;
        TenantId = tenantId;
        AppointmentId = appointmentId;
        ServiceId = serviceId;

        ServiceNameSnapshot = serviceNameSnapshot.Trim();
        PriceSnapshot = priceSnapshot;
        DurationMinutesSnapshot = durationMinutesSnapshot;
    }
}