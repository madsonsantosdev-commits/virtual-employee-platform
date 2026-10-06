using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Professionals;

namespace VirtualEmployee.Infrastructure.Persistence.Configurations;

public sealed class ProfessionalLocationConfiguration
    : IEntityTypeConfiguration<ProfessionalLocation>
{
    public void Configure(EntityTypeBuilder<ProfessionalLocation> builder)
    {
        builder.ToTable("professional_locations");

        builder.HasKey(x => new
        {
            x.TenantId,
            x.ProfessionalId,
            x.LocationId
        });

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uuid")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(x => x.ProfessionalId)
            .HasColumnName("professional_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.LocationId)
            .HasColumnName("location_id")
            .HasColumnType("uuid")
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

        builder.HasOne<Professional>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ProfessionalId
            })
            .HasPrincipalKey(x => new
            {
                x.TenantId,
                x.Id
            })
            .OnDelete(DeleteBehavior.Restrict);

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

        builder.HasIndex(x => new
        {
            x.TenantId,
            x.LocationId,
            x.IsActive
        });

        builder.HasIndex(x => new
        {
            x.TenantId,
            x.ProfessionalId,
            x.IsActive
        });
    }
}