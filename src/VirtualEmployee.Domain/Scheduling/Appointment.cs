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
    public string? CancellationReason { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
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

    public bool Cancel(
        AppointmentStatus cancellationStatus,
        string reason,
        DateTimeOffset now)
    {
        if (cancellationStatus is not
            (AppointmentStatus.CancelledByClient or
             AppointmentStatus.CancelledByBusiness))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cancellationStatus),
                "Cancellation status must identify client or business.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Cancellation reason cannot be empty.",
                nameof(reason));
        }

        var utcNow = now.ToUniversalTime();

        if (utcNow < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                "Cancellation cannot precede the last update.");
        }

                var normalizedReason = reason.Trim();

        if (normalizedReason.Length > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                "Cancellation reason cannot exceed 500 characters.");
        }

        if (Status == cancellationStatus)
        {
            return false;
        }

        if (!OccupiesSchedule)
        {
            throw new InvalidOperationException(
                "The current appointment status does not allow cancellation.");
        }

        if (Status == AppointmentStatus.Pending &&
            ReservationExpiresAt is { } expiresAt &&
            utcNow >= expiresAt)
        {
            throw new InvalidOperationException(
                "An overdue pending reservation must expire.");
        }

        Status = cancellationStatus;
        CancellationReason = normalizedReason;
        CancelledAt = utcNow;
        UpdatedAt = utcNow;

        return true;
    }
    public bool Reschedule(
        Guid locationId,
        Guid professionalId,
        DateTimeOffset startsAt,
        DateTimeOffset now)
    {
        ValidateIdentity(locationId, nameof(locationId));
        ValidateIdentity(professionalId, nameof(professionalId));

        var utcNow = now.ToUniversalTime();
        var utcStartsAt = startsAt.ToUniversalTime();

        if (utcNow < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                "Rescheduling cannot precede the last update.");
        }

        if (!OccupiesSchedule)
        {
            throw new InvalidOperationException(
                "The current appointment status does not allow rescheduling.");
        }

        if (StartsAt <= utcNow)
        {
            throw new InvalidOperationException(
                "An appointment that has started cannot be rescheduled.");
        }

        if (utcStartsAt <= utcNow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startsAt),
                "The new appointment start must be in the future.");
        }

        var newExpiresAt = ReservationExpiresAt;

        if (Status == AppointmentStatus.Pending)
        {
            if (ReservationExpiresAt is not { } expiresAt ||
                utcNow >= expiresAt)
            {
                throw new InvalidOperationException(
                    "An overdue pending reservation must expire.");
            }

            if (utcStartsAt < expiresAt)
            {
                newExpiresAt = utcStartsAt;
            }
        }

        // Calcula antes de alterar o agregado: overflow não deixa
        // unidade, profissional ou início parcialmente modificados.
        var newEndsAt = utcStartsAt.AddMinutes(
            TotalDurationMinutesSnapshot);

        if (LocationId == locationId &&
            ProfessionalId == professionalId &&
            StartsAt == utcStartsAt)
        {
            return false;
        }

        LocationId = locationId;
        ProfessionalId = professionalId;
        StartsAt = utcStartsAt;
        EndsAt = newEndsAt;
        ReservationExpiresAt = newExpiresAt;
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