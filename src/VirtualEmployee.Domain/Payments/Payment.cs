using VirtualEmployee.Domain.Common;
using VirtualEmployee.Domain.Scheduling;

namespace VirtualEmployee.Domain.Payments;

public sealed class Payment : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? ConfirmedPaymentAttemptId { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    private Payment(){}

    public Payment(
        Guid id,
        Appointment appointment,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment ID cannot be empty.",
                nameof(id));
        }

        ArgumentNullException.ThrowIfNull(appointment);

        var utcCreatedAt = createdAt.ToUniversalTime();

        if (utcCreatedAt < appointment.UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(createdAt),
                "Payment creation cannot precede the appointment's last update.");
        }

        if (appointment.Status != AppointmentStatus.Pending)
        {
            throw new InvalidOperationException(
                "Payment requires a pending appointment.");
        }

        if (appointment.ReservationExpiresAt is not { } expiresAt ||
            utcCreatedAt >= expiresAt)
        {
            throw new InvalidOperationException(
                "Payment requires an unexpired reservation.");
        }

        var amount = appointment.TotalPriceSnapshot;

        if (amount <= 0 ||
            amount > 9_999_999_999.99m ||
            decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(appointment),
                "Payment amount must be positive, have at most two decimal places and fit numeric(12,2).");
        }

        Id = id;
        TenantId = appointment.TenantId;
        BusinessId = appointment.BusinessId;
        AppointmentId = appointment.Id;

        Amount = amount;
        Status = PaymentStatus.Pending;

        CreatedAt = utcCreatedAt;
        UpdatedAt = utcCreatedAt;
    }

    public bool Confirm(
        PaymentAttempt attempt,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (attempt.TenantId != TenantId ||
            attempt.PaymentId != Id)
        {
            throw new ArgumentException(
                "The attempt must belong to this payment and tenant.",
                nameof(attempt));
        }

        if (attempt.Status != PaymentAttemptStatus.Confirmed ||
            attempt.ConfirmedAt is null ||
            attempt.Provider is null ||
            attempt.ProviderPaymentId is null)
        {
            throw new InvalidOperationException(
                "Payment confirmation requires a confirmed provider attempt.");
        }

        if (attempt.Amount != Amount)
        {
            throw new ArgumentException(
                "The attempt amount must match the full payment amount.",
                nameof(attempt));
        }

        if (Status == PaymentStatus.Confirmed)
        {
            if (ConfirmedPaymentAttemptId != attempt.Id)
            {
                throw new InvalidOperationException(
                    "The payment was already confirmed by another attempt.");
            }

            return false;
        }

        if (Status is not
            (PaymentStatus.Pending or
             PaymentStatus.Processing or
             PaymentStatus.Failed or
             PaymentStatus.Cancelled))
        {
            throw new InvalidOperationException(
                "The current payment status does not allow confirmation.");
        }

        var utcNow = now.ToUniversalTime();

        if (utcNow < UpdatedAt ||
            utcNow < attempt.UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                "Payment confirmation cannot precede the payment or attempt's last update.");
        }

        Status = PaymentStatus.Confirmed;
        ConfirmedPaymentAttemptId = attempt.Id;
        ConfirmedAt = utcNow;
        UpdatedAt = utcNow;

        return true;
    }
}