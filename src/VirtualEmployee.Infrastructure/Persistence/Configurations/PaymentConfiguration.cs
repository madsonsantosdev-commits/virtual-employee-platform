using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Domain.Scheduling;

namespace VirtualEmployee.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration
    : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", table =>
        {
            table.HasCheckConstraint(
                "CK_payments_amount",
                "amount > 0");

            table.HasCheckConstraint(
                "CK_payments_status",
                "status IN ('PENDING', 'PROCESSING', 'CONFIRMED', " +
                "'FAILED', 'CANCELLED', 'REFUNDED')");

            table.HasCheckConstraint(
                "CK_payments_timestamps",
                "updated_at >= created_at");

            table.HasCheckConstraint(
                "CK_payments_confirmation",
                "(status IN ('CONFIRMED', 'REFUNDED') AND " +
                "confirmed_payment_attempt_id IS NOT NULL AND " +
                "confirmed_at IS NOT NULL AND " +
                "confirmed_at >= created_at AND " +
                "confirmed_at <= updated_at) OR " +
                "(status NOT IN ('CONFIRMED', 'REFUNDED') AND " +
                "confirmed_payment_attempt_id IS NULL AND " +
                "confirmed_at IS NULL)");
        });

        builder.HasKey(payment => payment.Id);

        builder.HasAlternateKey(payment => new
        {
            payment.TenantId,
            payment.Id
        });

        builder.Property(payment => payment.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(payment => payment.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uuid")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(payment => payment.BusinessId)
            .HasColumnName("business_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(payment => payment.AppointmentId)
            .HasColumnName("appointment_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(payment => payment.Amount)
            .HasColumnName("amount")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(payment => payment.Status)
            .HasColumnName("status")
            .HasMaxLength(40)
            .HasConversion(
                status => ToDatabaseStatus(status),
                status => FromDatabaseStatus(status))
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(payment => payment.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(payment => payment.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(payment => payment.ConfirmedPaymentAttemptId)
            .HasColumnName("confirmed_payment_attempt_id")
            .HasColumnType("uuid");

        builder.Property(payment => payment.ConfirmedAt)
            .HasColumnName("confirmed_at")
            .HasColumnType("timestamp with time zone");

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(payment => new
            {
                payment.TenantId,
                payment.BusinessId,
                payment.AppointmentId
            })
            .HasPrincipalKey(appointment => new
            {
                appointment.TenantId,
                appointment.BusinessId,
                appointment.Id
            })
            .OnDelete(DeleteBehavior.Restrict);

        // A tentativa confirmadora deve pertencer ao próprio Payment.
        builder.HasOne<PaymentAttempt>()
            .WithMany()
            .HasForeignKey(payment => new
            {
                payment.TenantId,
                payment.Id,
                payment.ConfirmedPaymentAttemptId
            })
            .HasPrincipalKey(attempt => new
            {
                attempt.TenantId,
                attempt.PaymentId,
                attempt.Id
            })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(payment => new
        {
            payment.TenantId,
            payment.AppointmentId
        })
            .IsUnique();

        builder.HasIndex(payment => new
        {
            payment.TenantId,
            payment.BusinessId
        });
    }

    private static string ToDatabaseStatus(PaymentStatus status)
    {
        return status switch
        {
            PaymentStatus.Pending => "PENDING",
            PaymentStatus.Processing => "PROCESSING",
            PaymentStatus.Confirmed => "CONFIRMED",
            PaymentStatus.Failed => "FAILED",
            PaymentStatus.Cancelled => "CANCELLED",
            PaymentStatus.Refunded => "REFUNDED",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }

    private static PaymentStatus FromDatabaseStatus(string status)
    {
        return status switch
        {
            "PENDING" => PaymentStatus.Pending,
            "PROCESSING" => PaymentStatus.Processing,
            "CONFIRMED" => PaymentStatus.Confirmed,
            "FAILED" => PaymentStatus.Failed,
            "CANCELLED" => PaymentStatus.Cancelled,
            "REFUNDED" => PaymentStatus.Refunded,
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }
}