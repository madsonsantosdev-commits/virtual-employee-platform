using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualEmployee.Domain.Tenants;

namespace VirtualEmployee.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(tenant => tenant.Id);

        builder.Property(tenant => tenant.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(tenant => tenant.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();
    }
}