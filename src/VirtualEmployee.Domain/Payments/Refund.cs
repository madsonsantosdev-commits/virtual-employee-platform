using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Payments;

public sealed class Refund : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid PaymentAttemptId { get; private set; }
    public decimal Amount { get; private set; }
    public RefundReason Reason { get; private set; }
    public RefundStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? ProviderRefundId { get; private set; }

    private Refund() { }

    public Refund(
        Guid id,
        Payment payment,
        PaymentAttempt attempt,
        RefundReason reason,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(attempt);

        if (id == Guid.Empty)
        {
            throw new ArgumentException("Refund ID cannot be empty.", nameof(id));
        }

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        if (payment.Status != PaymentStatus.Confirmed ||
            payment.ConfirmedPaymentAttemptId != attempt.Id ||
            attempt.Status != PaymentAttemptStatus.Confirmed ||
            attempt.TenantId != payment.TenantId ||
            attempt.PaymentId != payment.Id ||
            attempt.Amount != payment.Amount)
        {
            throw new InvalidOperationException(
                "A full refund requires the attempt that confirmed the payment.");
        }

        if (now < payment.UpdatedAt || now < attempt.UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        Id = id;
        TenantId = payment.TenantId;
        PaymentId = payment.Id;
        PaymentAttemptId = attempt.Id;
        Amount = payment.Amount;
        Reason = reason;
        Status = RefundStatus.Requested;
        CreatedAt = UpdatedAt = now.ToUniversalTime();
    }

    public void MarkProcessing(DateTimeOffset now)
    {
        Transition(RefundStatus.Requested, RefundStatus.Processing, now);
    }

    public void Fail(DateTimeOffset now)
    {
        Transition(RefundStatus.Processing, RefundStatus.Failed, now);
    }

    public bool Complete(string providerRefundId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(providerRefundId) ||
            providerRefundId.Length > PaymentAttempt.ProviderPaymentIdMaxLength ||
            providerRefundId.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            throw new ArgumentException("Invalid provider refund ID.", nameof(providerRefundId));
        }

        if (Status == RefundStatus.Refunded && ProviderRefundId == providerRefundId)
        {
            return false;
        }

        Transition(RefundStatus.Processing, RefundStatus.Refunded, now);
        ProviderRefundId = providerRefundId;
        return true;
    }

    private void Transition(RefundStatus expected, RefundStatus next, DateTimeOffset now)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException("The current refund status does not allow this transition.");
        }

        if (now < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        Status = next;
        UpdatedAt = now.ToUniversalTime();
    }
}
