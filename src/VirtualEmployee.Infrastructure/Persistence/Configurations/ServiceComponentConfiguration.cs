using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Infrastructure.Persistence.Configurations;

public sealed class ServiceComponentConfiguration
    : IEntityTypeConfiguration<ServiceComponent>
{
    public void Configure(EntityTypeBuilder<ServiceComponent> builder)
    {
        builder.ToTable("service_components");

        builder.HasKey(x => new
        {
            x.TenantId,
            x.ComboServiceId,
            x.ComponentServiceId
        });

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uuid")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(x => x.ComboServiceId)
            .HasColumnName("combo_service_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.ComponentServiceId)
            .HasColumnName("component_service_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<Service>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.TenantId,
                x.ComboServiceId
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
                x.ComponentServiceId
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
            x.ComboServiceId,
            x.SortOrder
        });
    }
}