using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualEmployee.Domain.Payments;

namespace VirtualEmployee.Infrastructure.Persistence.Configurations;

public sealed class PaymentAttemptConfiguration
    : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("payment_attempts", table =>
        {
            table.HasCheckConstraint(
                "CK_payment_attempts_amount",
                "amount > 0");

            table.HasCheckConstraint(
                "CK_payment_attempts_method",
                "payment_method IN ('PIX', 'CREDIT_CARD', 'DEBIT_CARD')");

            table.HasCheckConstraint(
                "CK_payment_attempts_status",
                "status IN ('CREATED', 'PENDING', 'PROCESSING', " +
                "'CONFIRMED', 'FAILED', 'EXPIRED', 'CANCELLED')");

            table.HasCheckConstraint(
                "CK_payment_attempts_timestamps",
                "updated_at >= created_at AND expires_at > created_at");

            table.HasCheckConstraint(
                "CK_payment_attempts_expiration",
                "status <> 'EXPIRED' OR updated_at >= expires_at");

            table.HasCheckConstraint(
                "CK_payment_attempts_provider_pair",
                "(provider IS NULL AND provider_payment_id IS NULL) OR " +
                "(provider IS NOT NULL AND provider_payment_id IS NOT NULL)");

            table.HasCheckConstraint(
                "CK_payment_attempts_provider_references",
                "(provider IS NULL OR " +
                "(length(provider) > 0 AND " +
                "provider !~ '[[:space:][:cntrl:]]')) AND " +
                "(provider_payment_id IS NULL OR " +
                "(length(provider_payment_id) > 0 AND " +
                "provider_payment_id !~ '[[:space:][:cntrl:]]'))");

            table.HasCheckConstraint(
                "CK_payment_attempts_provider_state",
                "(status <> 'CREATED' OR provider IS NULL) AND " +
                "(status NOT IN ('PENDING', 'PROCESSING', 'CONFIRMED') " +
                "OR provider IS NOT NULL)");

            table.HasCheckConstraint(
                "CK_payment_attempts_confirmation",
                "(status = 'CONFIRMED' AND confirmed_at IS NOT NULL AND " +
                "confirmed_at >= created_at AND " +
                "confirmed_at <= updated_at) OR " +
                "(status <> 'CONFIRMED' AND confirmed_at IS NULL)");
        });

        builder.HasKey(attempt => attempt.Id);

        builder.HasAlternateKey(attempt => new
        {
            attempt.TenantId,
            attempt.PaymentId,
            attempt.Id
        });

        builder.Property(attempt => attempt.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(attempt => attempt.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uuid")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(attempt => attempt.PaymentId)
            .HasColumnName("payment_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(attempt => attempt.Amount)
            .HasColumnName("amount")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(attempt => attempt.PaymentMethod)
            .HasColumnName("payment_method")
            .HasMaxLength(40)
            .HasConversion(
                method => ToDatabaseMethod(method),
                method => FromDatabaseMethod(method))
            .IsRequired();

        builder.Property(attempt => attempt.Status)
            .HasColumnName("status")
            .HasMaxLength(40)
            .HasConversion(
                status => ToDatabaseStatus(status),
                status => FromDatabaseStatus(status))
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(attempt => attempt.Provider)
            .HasColumnName("provider")
            .HasMaxLength(PaymentAttempt.ProviderMaxLength)
            .IsConcurrencyToken();

        builder.Property(attempt => attempt.ProviderPaymentId)
            .HasColumnName("provider_payment_id")
            .HasMaxLength(PaymentAttempt.ProviderPaymentIdMaxLength)
            .IsConcurrencyToken();

        builder.Property(attempt => attempt.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(attempt => attempt.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(attempt => attempt.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(attempt => attempt.ConfirmedAt)
            .HasColumnName("confirmed_at")
            .HasColumnType("timestamp with time zone");

        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(attempt => new
            {
                attempt.TenantId,
                attempt.PaymentId,
                attempt.Amount
            })
            .HasPrincipalKey(payment => new
            {
                payment.TenantId,
                payment.Id,
                payment.Amount
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(attempt => new
        {
            attempt.TenantId,
            attempt.PaymentId,
            attempt.CreatedAt
        });

        builder.HasIndex(attempt => new
        {
            attempt.Provider,
            attempt.ProviderPaymentId
        })
            .IsUnique()
            .HasFilter("provider_payment_id IS NOT NULL");

        builder.HasIndex(attempt => new
        {
            attempt.TenantId,
            attempt.Status,
            attempt.ExpiresAt
        });
    }

    private static string ToDatabaseMethod(PaymentMethod method)
    {
        return method switch
        {
            PaymentMethod.Pix => "PIX",
            PaymentMethod.CreditCard => "CREDIT_CARD",
            PaymentMethod.DebitCard => "DEBIT_CARD",
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
    }

    private static PaymentMethod FromDatabaseMethod(string method)
    {
        return method switch
        {
            "PIX" => PaymentMethod.Pix,
            "CREDIT_CARD" => PaymentMethod.CreditCard,
            "DEBIT_CARD" => PaymentMethod.DebitCard,
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
    }

    private static string ToDatabaseStatus(PaymentAttemptStatus status)
    {
        return status switch
        {
            PaymentAttemptStatus.Created => "CREATED",
            PaymentAttemptStatus.Pending => "PENDING",
            PaymentAttemptStatus.Processing => "PROCESSING",
            PaymentAttemptStatus.Confirmed => "CONFIRMED",
            PaymentAttemptStatus.Failed => "FAILED",
            PaymentAttemptStatus.Expired => "EXPIRED",
            PaymentAttemptStatus.Cancelled => "CANCELLED",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }

    private static PaymentAttemptStatus FromDatabaseStatus(string status)
    {
        return status switch
        {
            "CREATED" => PaymentAttemptStatus.Created,
            "PENDING" => PaymentAttemptStatus.Pending,
            "PROCESSING" => PaymentAttemptStatus.Processing,
            "CONFIRMED" => PaymentAttemptStatus.Confirmed,
            "FAILED" => PaymentAttemptStatus.Failed,
            "EXPIRED" => PaymentAttemptStatus.Expired,
            "CANCELLED" => PaymentAttemptStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }
}