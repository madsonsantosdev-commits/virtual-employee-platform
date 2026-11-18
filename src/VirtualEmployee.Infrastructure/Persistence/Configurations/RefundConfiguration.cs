using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualEmployee.Domain.Payments;

namespace VirtualEmployee.Infrastructure.Persistence.Configurations;

public sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("refunds", table =>
        {
            table.HasCheckConstraint("CK_refunds_amount", "amount > 0");
            table.HasCheckConstraint("CK_refunds_timestamps", "updated_at >= created_at");
            table.HasCheckConstraint(
                "CK_refunds_status",
                "status IN ('REFUND_REQUESTED', 'REFUND_PROCESSING', 'REFUNDED', 'REFUND_FAILED')");
            table.HasCheckConstraint(
                "CK_refunds_reason",
                "reason IN ('CUSTOMER_CANCELLATION', 'BUSINESS_CANCELLATION', 'LATE_PAYMENT')");
            table.HasCheckConstraint(
                "CK_refunds_provider_reference",
                "(status = 'REFUNDED' AND provider_refund_id IS NOT NULL AND " +
                "provider_refund_id <> '' AND provider_refund_id !~ '[[:space:][:cntrl:]]') OR " +
                "(status <> 'REFUNDED' AND provider_refund_id IS NULL)");
        });

        builder.HasKey(refund => refund.Id);
        builder.Property(refund => refund.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(refund => refund.TenantId).HasColumnName("tenant_id").IsConcurrencyToken();
        builder.Property(refund => refund.PaymentId).HasColumnName("payment_id");
        builder.Property(refund => refund.PaymentAttemptId).HasColumnName("payment_attempt_id");
        builder.Property(refund => refund.Amount).HasColumnName("amount").HasPrecision(12, 2);
        builder.Property(refund => refund.CreatedAt).HasColumnName("created_at");
        builder.Property(refund => refund.UpdatedAt).HasColumnName("updated_at").IsConcurrencyToken();
        builder.Property(refund => refund.ProviderRefundId)
            .HasColumnName("provider_refund_id")
            .HasMaxLength(PaymentAttempt.ProviderPaymentIdMaxLength);

        builder.Property(refund => refund.Reason)
            .HasColumnName("reason")
            .HasMaxLength(40)
            .HasConversion(reason => ToReason(reason), value => FromReason(value));
        builder.Property(refund => refund.Status)
            .HasColumnName("status")
            .HasMaxLength(40)
            .HasConversion(status => ToStatus(status), value => FromStatus(value))
            .IsConcurrencyToken();

        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(refund => new { refund.TenantId, refund.PaymentId, refund.Amount })
            .HasPrincipalKey(payment => new { payment.TenantId, payment.Id, payment.Amount })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PaymentAttempt>()
            .WithMany()
            .HasForeignKey(refund => new { refund.TenantId, refund.PaymentId, refund.PaymentAttemptId })
            .HasPrincipalKey(attempt => new { attempt.TenantId, attempt.PaymentId, attempt.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(refund => new { refund.TenantId, refund.PaymentId })
            .IsUnique()
            .HasFilter("status IN ('REFUND_REQUESTED', 'REFUND_PROCESSING', 'REFUNDED')");
    }

    private static string ToReason(RefundReason reason) => reason switch
    {
        RefundReason.CustomerCancellation => "CUSTOMER_CANCELLATION",
        RefundReason.BusinessCancellation => "BUSINESS_CANCELLATION",
        RefundReason.LatePayment => "LATE_PAYMENT",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    };

    private static RefundReason FromReason(string value) => value switch
    {
        "CUSTOMER_CANCELLATION" => RefundReason.CustomerCancellation,
        "BUSINESS_CANCELLATION" => RefundReason.BusinessCancellation,
        "LATE_PAYMENT" => RefundReason.LatePayment,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string ToStatus(RefundStatus status) => status switch
    {
        RefundStatus.Requested => "REFUND_REQUESTED",
        RefundStatus.Processing => "REFUND_PROCESSING",
        RefundStatus.Refunded => "REFUNDED",
        RefundStatus.Failed => "REFUND_FAILED",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static RefundStatus FromStatus(string value) => value switch
    {
        "REFUND_REQUESTED" => RefundStatus.Requested,
        "REFUND_PROCESSING" => RefundStatus.Processing,
        "REFUNDED" => RefundStatus.Refunded,
        "REFUND_FAILED" => RefundStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
