using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Domain.Tenants;

namespace VirtualEmployee.Infrastructure.Persistence.Configurations;

public sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services");

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

        builder.Property(x => x.BusinessId)
            .HasColumnName("business_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(x => x.ServiceType)
            .HasColumnName("service_type")
            .HasMaxLength(20)
            .HasConversion(
                value => value == ServiceType.Single
                    ? "SINGLE"
                    : "COMBO",
                value => value == "SINGLE"
                    ? ServiceType.Single
                    : ServiceType.Combo)
            .IsRequired();

        builder.Property(x => x.Price)
            .HasColumnName("price")
            .HasPrecision(12, 2)
            .IsRequired();

        builder.Property(x => x.DurationMinutes)
            .HasColumnName("duration_minutes")
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

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Business>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.BusinessId
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
            x.BusinessId,
            x.IsActive
        });
    }
}