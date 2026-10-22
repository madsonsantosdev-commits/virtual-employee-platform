using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Scheduling;

public sealed class AppointmentHistoryEntry : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AppointmentId { get; private set; }

    public AppointmentHistoryType Type { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public AppointmentHistoryState? Before { get; private set; }
    public AppointmentHistoryState After { get; private set; } = null!;
    public string? Reason { get; private set; }

    private AppointmentHistoryEntry()
    {
    }

    public AppointmentHistoryEntry(
        Guid id,
        Guid tenantId,
        Guid appointmentId,
        AppointmentHistoryType type,
        DateTimeOffset occurredAt,
        AppointmentHistoryState? before,
        AppointmentHistoryState after,
        string? reason = null)
    {
        ValidateIdentity(id, nameof(id));
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(appointmentId, nameof(appointmentId));

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        ArgumentNullException.ThrowIfNull(after);

        ValidateState(after, nameof(after));

        if (before is not null)
        {
            ValidateState(before, nameof(before));
        }

        var normalizedReason = reason?.Trim();

        if (type == AppointmentHistoryType.Cancelled)
        {
            if (string.IsNullOrWhiteSpace(normalizedReason) ||
                normalizedReason.Length > 500)
            {
                throw new ArgumentException(
                    "Cancellation history requires a reason with " +
                    "between 1 and 500 characters.",
                    nameof(reason));
            }
        }
        else if (reason is not null)
        {
            throw new ArgumentException(
                "Only cancellation history accepts a reason.",
                nameof(reason));
        }

        var utcOccurredAt = occurredAt.ToUniversalTime();

        ValidateTransition(type, before, after, utcOccurredAt);

        Id = id;
        TenantId = tenantId;
        AppointmentId = appointmentId;
        Type = type;
        OccurredAt = utcOccurredAt;
        Before = before;
        After = after;
        Reason = normalizedReason;
    }

    private static void ValidateTransition(
        AppointmentHistoryType type,
        AppointmentHistoryState? before,
        AppointmentHistoryState after,
        DateTimeOffset occurredAt)
    {
        if (type == AppointmentHistoryType.Created)
        {
            if (before is not null ||
                after.Status != AppointmentStatus.Pending ||
                after.ReservationExpiresAt is not { } expiresAt ||
                expiresAt <= occurredAt)
            {
                throw new ArgumentException(
                    "Creation history requires no previous state " +
                    "and a pending reservation with a valid deadline.");
            }

            return;
        }

        if (before is null)
        {
            throw new ArgumentException(
                "This history operation requires a previous state.",
                nameof(before));
        }

        var occupiesSchedule = before.Status is
            AppointmentStatus.Pending or
            AppointmentStatus.Confirmed or
            AppointmentStatus.ConfirmedByClient or
            AppointmentStatus.RescheduleRequested;

        var valid = type switch
        {
            AppointmentHistoryType.Cancelled =>
                occupiesSchedule &&
                after.Status is
                    (AppointmentStatus.CancelledByClient or
                     AppointmentStatus.CancelledByBusiness) &&
                after == before with { Status = after.Status } &&
                (before.Status != AppointmentStatus.Pending ||
                 before.ReservationExpiresAt is { } deadline &&
                 occurredAt < deadline),

            AppointmentHistoryType.Expired =>
                before.Status == AppointmentStatus.Pending &&
                after.Status == AppointmentStatus.Expired &&
                after == before with { Status = AppointmentStatus.Expired } &&
                before.ReservationExpiresAt is { } expiration &&
                occurredAt >= expiration,

            AppointmentHistoryType.Rescheduled =>
                occupiesSchedule &&
                before.Status == after.Status &&
                before != after &&
                before.StartsAt > occurredAt &&
                after.StartsAt > occurredAt &&
                before.EndsAt - before.StartsAt ==
                    after.EndsAt - after.StartsAt &&
                (before.Status == AppointmentStatus.Pending
                    ? before.ReservationExpiresAt is { } oldExpiration &&
                      occurredAt < oldExpiration &&
                      after.ReservationExpiresAt ==
                          (after.StartsAt < oldExpiration
                              ? after.StartsAt
                              : oldExpiration)
                    : after.ReservationExpiresAt ==
                        before.ReservationExpiresAt),

            _ => false
        };

        if (!valid)
        {
            throw new ArgumentException(
                "The states do not represent a valid history operation.");
        }
    }

    private static void ValidateState(
        AppointmentHistoryState state,
        string parameterName)
    {
        if (!Enum.IsDefined(state.Status) ||
            state.LocationId == Guid.Empty ||
            state.ProfessionalId == Guid.Empty ||
            state.StartsAt >= state.EndsAt ||
            state.StartsAt.Offset != TimeSpan.Zero ||
            state.EndsAt.Offset != TimeSpan.Zero ||
            state.ReservationExpiresAt is { } expiration &&
                (expiration.Offset != TimeSpan.Zero ||
                 expiration > state.StartsAt) ||
            state.Status == AppointmentStatus.Pending &&
                state.ReservationExpiresAt is null)
        {
            throw new ArgumentException(
                "History state contains invalid operational data.",
                parameterName);
        }
    }

    private static void ValidateIdentity(
        Guid value,
        string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "History identity cannot be empty.",
                parameterName);
        }
    }
}