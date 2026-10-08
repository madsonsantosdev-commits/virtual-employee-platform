using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Infrastructure.Persistence.Configurations;

public sealed class ProfessionalServiceConfiguration
    : IEntityTypeConfiguration<ProfessionalService>
{
    public void Configure(
        EntityTypeBuilder<ProfessionalService> builder)
    {
        builder.ToTable("professional_services");

        builder.HasKey(x => new
        {
            x.TenantId,
            x.ProfessionalId,
            x.ServiceId
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

        builder.Property(x => x.ServiceId)
            .HasColumnName("service_id")
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

        builder.HasOne<Service>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ServiceId
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
            x.ProfessionalId,
            x.IsActive
        });
    }
}