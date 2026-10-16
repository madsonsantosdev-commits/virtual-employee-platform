using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Infrastructure.Persistence.Configurations;

public sealed class AppointmentItemConfiguration
    : IEntityTypeConfiguration<AppointmentItem>
{
    public void Configure(EntityTypeBuilder<AppointmentItem> builder)
    {
        builder.ToTable("appointment_items", table =>
        {
            table.HasCheckConstraint(
                "CK_appointment_items_service_name",
                "length(btrim(service_name_snapshot)) > 0");

            table.HasCheckConstraint(
                "CK_appointment_items_price",
                "price_snapshot >= 0");

            table.HasCheckConstraint(
                "CK_appointment_items_duration",
                "duration_minutes_snapshot > 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uuid")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(x => x.AppointmentId)
            .HasColumnName("appointment_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.ServiceId)
            .HasColumnName("service_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.ServiceNameSnapshot)
            .HasColumnName("service_name_snapshot")
            .IsRequired();

        builder.Property(x => x.PriceSnapshot)
            .HasColumnName("price_snapshot")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(x => x.DurationMinutesSnapshot)
            .HasColumnName("duration_minutes_snapshot")
            .IsRequired();

        builder.HasOne<Service>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ServiceId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.TenantId,
            x.AppointmentId,
            x.ServiceId
        })
            .IsUnique();
    }
}