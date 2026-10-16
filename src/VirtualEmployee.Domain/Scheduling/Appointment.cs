using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Scheduling;

public sealed class Appointment : ITenantScoped
{
    private readonly List<AppointmentItem> _items = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid ProfessionalId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }

    public decimal TotalPriceSnapshot { get; private set; }
    public int TotalDurationMinutesSnapshot { get; private set; }

    public AppointmentStatus Status { get; private set; }
    public DateTimeOffset? ReservationExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<AppointmentItem> Items => _items.AsReadOnly();

    public bool OccupiesSchedule =>
        Status is AppointmentStatus.Pending
            or AppointmentStatus.Confirmed
            or AppointmentStatus.ConfirmedByClient
            or AppointmentStatus.RescheduleRequested;

    private Appointment()
    {
    }

    public Appointment(
        Guid id,
        Guid tenantId,
        Guid businessId,
        Guid locationId,
        Guid customerId,
        Guid professionalId,
        DateTimeOffset startsAt,
        DateTimeOffset createdAt,
        TimeSpan reservationLifetime,
        IEnumerable<AppointmentItem> items)
    {
        ValidateIdentity(id, nameof(id));
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(businessId, nameof(businessId));
        ValidateIdentity(locationId, nameof(locationId));
        ValidateIdentity(customerId, nameof(customerId));
        ValidateIdentity(professionalId, nameof(professionalId));

        ArgumentNullException.ThrowIfNull(items);

        var utcCreatedAt = createdAt.ToUniversalTime();
        var utcStartsAt = startsAt.ToUniversalTime();

        if (utcStartsAt <= utcCreatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startsAt),
                "Appointment must start after its creation.");
        }

        if (reservationLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reservationLifetime),
                "Reservation lifetime must be greater than zero.");
        }

        var selectedItems = items.ToArray();

        if (selectedItems.Length == 0)
        {
            throw new ArgumentException(
                "Appointment must contain at least one item.",
                nameof(items));
        }

        if (selectedItems.Any(item => item is null))
        {
            throw new ArgumentException(
                "Appointment items cannot contain null.",
                nameof(items));
        }

        if (selectedItems.Any(item =>
                item.TenantId != tenantId ||
                item.AppointmentId != id))
        {
            throw new ArgumentException(
                "Items must belong to the appointment and its tenant.",
                nameof(items));
        }

        if (selectedItems.Select(item => item.Id).Distinct().Count()
                != selectedItems.Length ||
            selectedItems.Select(item => item.ServiceId).Distinct().Count()
                != selectedItems.Length)
        {
            throw new ArgumentException(
                "Appointment items and services cannot be duplicated.",
                nameof(items));
        }

        var totalPrice = selectedItems.Sum(item => item.PriceSnapshot);
        var totalDuration = selectedItems.Sum(
            item => (long)item.DurationMinutesSnapshot);

        if (totalDuration > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(items),
                "Total duration exceeds the supported limit.");
        }

        var endsAt = utcStartsAt.AddMinutes((int)totalDuration);
        var expiresAt = utcCreatedAt.Add(reservationLifetime);

        // A reserva pendente não deve ultrapassar o início do atendimento.
        if (expiresAt > utcStartsAt)
        {
            expiresAt = utcStartsAt;
        }

        Id = id;
        TenantId = tenantId;
        BusinessId = businessId;
        LocationId = locationId;
        CustomerId = customerId;
        ProfessionalId = professionalId;

        StartsAt = utcStartsAt;
        EndsAt = endsAt;

        TotalPriceSnapshot = totalPrice;
        TotalDurationMinutesSnapshot = (int)totalDuration;

        Status = AppointmentStatus.Pending;
        ReservationExpiresAt = expiresAt;

        CreatedAt = utcCreatedAt;
        UpdatedAt = utcCreatedAt;

        _items.AddRange(selectedItems);
    }

    public bool Expire(DateTimeOffset now)
    {
        var utcNow = now.ToUniversalTime();

        if (Status != AppointmentStatus.Pending ||
            ReservationExpiresAt is not { } expiresAt ||
            utcNow < expiresAt)
        {
            return false;
        }

        Status = AppointmentStatus.Expired;
        UpdatedAt = utcNow;

        return true;
    }

    private static void ValidateIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Appointment identity cannot be empty.",
                parameterName);
        }
    }
}