using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.Customers;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Scheduling;

namespace VirtualEmployee.Infrastructure.Persistence.Configurations;

public sealed class AppointmentConfiguration
    : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments", table =>
        {
            table.HasCheckConstraint(
                "CK_appointments_time_window",
                "starts_at < ends_at");

            table.HasCheckConstraint(
                "CK_appointments_total_price",
                "total_price_snapshot >= 0");

            table.HasCheckConstraint(
                "CK_appointments_total_duration",
                "total_duration_minutes_snapshot > 0");

            table.HasCheckConstraint(
                "CK_appointments_duration_matches_window",
                "ends_at = starts_at + " +
                "total_duration_minutes_snapshot * interval '1 minute'");

            table.HasCheckConstraint(
                "CK_appointments_status",
                "status IN ('PENDING', 'CONFIRMED', " +
                "'CONFIRMED_BY_CLIENT', 'RESCHEDULE_REQUESTED', " +
                "'CANCELLED_BY_CLIENT', 'CANCELLED_BY_BUSINESS', " +
                "'COMPLETED', 'NO_SHOW', 'EXPIRED')");

            table.HasCheckConstraint(
                "CK_appointments_pending_expiration",
                "status <> 'PENDING' OR " +
                "(reservation_expires_at IS NOT NULL AND " +
                "reservation_expires_at > created_at AND " +
                "reservation_expires_at <= starts_at)");

            table.HasCheckConstraint(
                "CK_appointments_cancellation",
                "(" +
                "status IN ('CANCELLED_BY_CLIENT', 'CANCELLED_BY_BUSINESS') " +
                "AND cancellation_reason IS NOT NULL " +
                "AND length(btrim(cancellation_reason)) > 0 " +
                "AND cancelled_at IS NOT NULL " +
                "AND cancelled_at >= created_at " +
                "AND cancelled_at <= updated_at" +
                ") OR (" +
                "status NOT IN ('CANCELLED_BY_CLIENT', 'CANCELLED_BY_BUSINESS') " +
                "AND cancellation_reason IS NULL " +
                "AND cancelled_at IS NULL" +
                ")");
        });

        builder.HasKey(x => x.Id);

        builder.HasAlternateKey(x => new { x.TenantId, x.Id });

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uuid")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(x => x.BusinessId)
            .HasColumnName("business_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.LocationId)
            .HasColumnName("location_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.CustomerId)
            .HasColumnName("customer_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.ProfessionalId)
            .HasColumnName("professional_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.StartsAt)
            .HasColumnName("starts_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.EndsAt)
            .HasColumnName("ends_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.TotalPriceSnapshot)
            .HasColumnName("total_price_snapshot")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(x => x.TotalDurationMinutesSnapshot)
            .HasColumnName("total_duration_minutes_snapshot")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasMaxLength(40)
            .HasConversion(
                value => ToDatabaseStatus(value),
                value => FromDatabaseStatus(value))
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(x => x.ReservationExpiresAt)
            .HasColumnName("reservation_expires_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(500);

        builder.Property(x => x.CancelledAt)
            .HasColumnName("cancelled_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Ignore(x => x.OccupiesSchedule);

        builder.HasOne<Business>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.BusinessId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CustomerId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ProfessionalLocation>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ProfessionalId,
                x.LocationId
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.ProfessionalId,
                x.LocationId
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => new { x.TenantId, x.AppointmentId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(x => x.Items)
            .HasField("_items")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new
        {
            x.TenantId,
            x.LocationId,
            x.StartsAt
        });

        builder.HasIndex(x => new
        {
            x.TenantId,
            x.ProfessionalId,
            x.StartsAt
        });

        builder.HasIndex(x => new
        {
            x.TenantId,
            x.CustomerId,
            x.StartsAt
        });

        builder.HasIndex(x => new
        {
            x.TenantId,
            x.Status,
            x.ReservationExpiresAt
        });
    }

    private static string ToDatabaseStatus(AppointmentStatus status)
    {
        return status switch
        {
            AppointmentStatus.Pending => "PENDING",
            AppointmentStatus.Confirmed => "CONFIRMED",
            AppointmentStatus.ConfirmedByClient => "CONFIRMED_BY_CLIENT",
            AppointmentStatus.RescheduleRequested => "RESCHEDULE_REQUESTED",
            AppointmentStatus.CancelledByClient => "CANCELLED_BY_CLIENT",
            AppointmentStatus.CancelledByBusiness => "CANCELLED_BY_BUSINESS",
            AppointmentStatus.Completed => "COMPLETED",
            AppointmentStatus.NoShow => "NO_SHOW",
            AppointmentStatus.Expired => "EXPIRED",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }

    private static AppointmentStatus FromDatabaseStatus(string status)
    {
        return status switch
        {
            "PENDING" => AppointmentStatus.Pending,
            "CONFIRMED" => AppointmentStatus.Confirmed,
            "CONFIRMED_BY_CLIENT" => AppointmentStatus.ConfirmedByClient,
            "RESCHEDULE_REQUESTED" => AppointmentStatus.RescheduleRequested,
            "CANCELLED_BY_CLIENT" => AppointmentStatus.CancelledByClient,
            "CANCELLED_BY_BUSINESS" => AppointmentStatus.CancelledByBusiness,
            "COMPLETED" => AppointmentStatus.Completed,
            "NO_SHOW" => AppointmentStatus.NoShow,
            "EXPIRED" => AppointmentStatus.Expired,
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }
}