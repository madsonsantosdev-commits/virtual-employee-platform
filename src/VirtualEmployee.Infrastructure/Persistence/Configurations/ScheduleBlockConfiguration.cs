using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Scheduling;

namespace VirtualEmployee.Infrastructure.Persistence.Configurations;

public sealed class ScheduleBlockConfiguration
    : IEntityTypeConfiguration<ScheduleBlock>
{
    public void Configure(EntityTypeBuilder<ScheduleBlock> builder)
    {
        builder.ToTable(
            "schedule_blocks",
            table => table.HasCheckConstraint(
                "CK_schedule_blocks_time_window",
                "starts_at < ends_at"));

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
            .IsRequired(false);

        builder.Property(x => x.StartsAt)
            .HasColumnName("starts_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.EndsAt)
            .HasColumnName("ends_at")
            .HasColumnType("timestamp with time zone")
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

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.LocationId
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.Id
            })
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
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.TenantId,
            x.LocationId,
            x.ProfessionalId,
            x.StartsAt,
            x.EndsAt
        });
    }
}