using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Payments;

public sealed class PaymentAttempt : ITenantScoped
{
    public const int ProviderMaxLength = 100;
    public const int ProviderPaymentIdMaxLength = 200;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PaymentId { get; private set; }

    public PaymentMethod PaymentMethod { get; private set; }
    public PaymentAttemptStatus Status { get; private set; }

    public decimal Amount { get; private set; }

    public string? Provider { get; private set; }
    public string? ProviderPaymentId { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }

    private PaymentAttempt()
    {
    }

    public PaymentAttempt(
        Guid id,
        Payment payment,
        PaymentMethod paymentMethod,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment attempt ID cannot be empty.",
                nameof(id));
        }

        ArgumentNullException.ThrowIfNull(payment);

        if (!Enum.IsDefined(paymentMethod))
        {
            throw new ArgumentOutOfRangeException(
                nameof(paymentMethod),
                "Payment method is invalid.");
        }

        if (payment.Status is not
            (PaymentStatus.Pending or
             PaymentStatus.Processing or
             PaymentStatus.Failed))
        {
            throw new InvalidOperationException(
                "The current payment status does not allow a new attempt.");
        }

        var utcCreatedAt = createdAt.ToUniversalTime();
        var utcExpiresAt = expiresAt.ToUniversalTime();

        if (utcCreatedAt < payment.UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(createdAt),
                "Attempt creation cannot precede the payment's last update.");
        }

        if (utcExpiresAt <= utcCreatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                "Attempt expiration must be after its creation.");
        }

        Id = id;
        TenantId = payment.TenantId;
        PaymentId = payment.Id;

        PaymentMethod = paymentMethod;
        Status = PaymentAttemptStatus.Created;
        Amount = payment.Amount;

        ExpiresAt = utcExpiresAt;
        CreatedAt = utcCreatedAt;
        UpdatedAt = utcCreatedAt;
    }

    public bool RegisterProviderPayment(
        string provider,
        string providerPaymentId,
        DateTimeOffset now)
    {
        var normalizedProvider = ValidateReference(
            provider,
            ProviderMaxLength,
            nameof(provider));

        var normalizedPaymentId = ValidateReference(
            providerPaymentId,
            ProviderPaymentIdMaxLength,
            nameof(providerPaymentId));

        var utcNow = now.ToUniversalTime();

        if (Provider is not null || ProviderPaymentId is not null)
        {
            if (!string.Equals(
                    Provider,
                    normalizedProvider,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    ProviderPaymentId,
                    normalizedPaymentId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The attempt is already associated with another provider payment.");
            }

            return false;
        }

        if (utcNow < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                "Provider registration cannot precede the last update.");
        }

        if (Status is not
            (PaymentAttemptStatus.Created or
             PaymentAttemptStatus.Failed or
             PaymentAttemptStatus.Expired or
             PaymentAttemptStatus.Cancelled))
        {
            throw new InvalidOperationException(
                "The current attempt status does not allow initial provider registration.");
        }

        Provider = normalizedProvider;
        ProviderPaymentId = normalizedPaymentId;

        if (Status == PaymentAttemptStatus.Created)
        {
            Status = PaymentAttemptStatus.Pending;
        }

        UpdatedAt = utcNow;

        return true;
    }

    public bool MarkProcessing(DateTimeOffset now)
    {
        if (Status == PaymentAttemptStatus.Processing)
        {
            return false;
        }

        if (Status != PaymentAttemptStatus.Pending ||
            Provider is null ||
            ProviderPaymentId is null)
        {
            throw new InvalidOperationException(
                "Only a pending attempt associated with a provider payment can start processing.");
        }

        var utcNow = now.ToUniversalTime();

        if (utcNow < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                "Processing cannot precede the last update.");
        }

        Status = PaymentAttemptStatus.Processing;
        UpdatedAt = utcNow;

        return true;
    }

    public bool Confirm(
        decimal confirmedAmount,
        DateTimeOffset now)
    {
        if (confirmedAmount != Amount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confirmedAmount),
                "Confirmed amount must match the full attempt amount.");
        }

        if (Provider is null || ProviderPaymentId is null)
        {
            throw new InvalidOperationException(
                "Confirmation requires an associated provider payment.");
        }

        if (Status == PaymentAttemptStatus.Confirmed)
        {
            return false;
        }

        if (Status is not
            (PaymentAttemptStatus.Pending or
             PaymentAttemptStatus.Processing or
             PaymentAttemptStatus.Failed or
             PaymentAttemptStatus.Expired or
             PaymentAttemptStatus.Cancelled))
        {
            throw new InvalidOperationException(
                "The current attempt status does not allow confirmation.");
        }

        var utcNow = now.ToUniversalTime();

        if (utcNow < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                "Confirmation cannot precede the last update.");
        }

        Status = PaymentAttemptStatus.Confirmed;
        ConfirmedAt = utcNow;
        UpdatedAt = utcNow;

        return true;
    }

    public bool Fail(DateTimeOffset now)
    {
        if (Status == PaymentAttemptStatus.Failed)
        {
            return false;
        }

        if (Status is not
            (PaymentAttemptStatus.Created or
             PaymentAttemptStatus.Pending or
             PaymentAttemptStatus.Processing))
        {
            throw new InvalidOperationException(
                "The current attempt status does not allow failure.");
        }

        var utcNow = now.ToUniversalTime();

        if (utcNow < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                "Failure cannot precede the last update.");
        }

        Status = PaymentAttemptStatus.Failed;
        UpdatedAt = utcNow;

        return true;
    }

    public bool Expire(DateTimeOffset now)
    {
        if (Status is not
            (PaymentAttemptStatus.Created or
             PaymentAttemptStatus.Pending or
             PaymentAttemptStatus.Processing))
        {
            return false;
        }

        var utcNow = now.ToUniversalTime();

        if (utcNow < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                "Expiration cannot precede the last update.");
        }

        if (utcNow < ExpiresAt)
        {
            return false;
        }

        Status = PaymentAttemptStatus.Expired;
        UpdatedAt = utcNow;

        return true;
    }

    public bool Cancel(DateTimeOffset now)
    {
        if (Status == PaymentAttemptStatus.Cancelled)
        {
            return false;
        }

        if (Status is not
            (PaymentAttemptStatus.Created or
             PaymentAttemptStatus.Pending or
             PaymentAttemptStatus.Processing))
        {
            throw new InvalidOperationException(
                "The current attempt status does not allow cancellation.");
        }

        var utcNow = now.ToUniversalTime();

        if (utcNow < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                "Cancellation cannot precede the last update.");
        }

        Status = PaymentAttemptStatus.Cancelled;
        UpdatedAt = utcNow;

        return true;
    }

    private static string ValidateReference(
        string value,
        int maxLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Provider references cannot be empty.",
                parameterName);
        }

        var normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Provider references cannot exceed {maxLength} characters.");
        }

        if (normalized.Any(character =>
                char.IsWhiteSpace(character) ||
                char.IsControl(character)))
        {
            throw new ArgumentException(
                "Provider references cannot contain whitespace or control characters.",
                parameterName);
        }

        return normalized;
    }
}