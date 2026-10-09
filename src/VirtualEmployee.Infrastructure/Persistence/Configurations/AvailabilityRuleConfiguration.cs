using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Scheduling;

namespace VirtualEmployee.Infrastructure.Persistence.Configurations;

public sealed class AvailabilityRuleConfiguration
    : IEntityTypeConfiguration<AvailabilityRule>
{
    public void Configure(EntityTypeBuilder<AvailabilityRule> builder)
    {
        builder.ToTable("availability_rules", table =>
        {
            table.HasCheckConstraint(
                "CK_availability_rules_day_of_week",
                "day_of_week BETWEEN 0 AND 6");

            table.HasCheckConstraint(
                "CK_availability_rules_time_window",
                "start_time < end_time");
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

        builder.Property(x => x.LocationId)
            .HasColumnName("location_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.ProfessionalId)
            .HasColumnName("professional_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.DayOfWeek)
            .HasColumnName("day_of_week")
            .HasConversion<int>()
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(x => x.StartTime)
            .HasColumnName("start_time")
            .HasColumnType("time without time zone")
            .IsRequired();

        builder.Property(x => x.EndTime)
            .HasColumnName("end_time")
            .HasColumnType("time without time zone")
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<ProfessionalLocation>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ProfessionalId,
                x.LocationId
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.TenantId,
            x.LocationId,
            x.ProfessionalId,
            x.DayOfWeek,
            x.IsActive
        });
    }
}