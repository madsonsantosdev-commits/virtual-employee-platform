using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualEmployee.Domain.BusinessTypes;

namespace VirtualEmployee.Infrastructure.Persistence.Configurations;

public sealed class BusinessTypeConfiguration
    : IEntityTypeConfiguration<BusinessType>
{
    public void Configure(EntityTypeBuilder<BusinessType> builder)
    {
        builder.ToTable("business_types");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(x => x.Code)
            .HasColumnName("code")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.IsSystem)
            .HasColumnName("is_system")
            .HasDefaultValue(false)
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

        builder.HasIndex(x => x.Code)
            .IsUnique();

        // Catálogo oficial da plataforma.
        // A data é fixa para manter as migrations determinísticas.
        var seedDate = new DateTimeOffset(
            2026, 9, 23,
            0, 0, 0,
            TimeSpan.Zero);

        builder.HasData(
            new
            {
                Id = BusinessTypeIds.Barbershop,
                Code = "BARBERSHOP",
                Name = "Barbearia",
                IsSystem = true,
                IsActive = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
            },
            new
            {
                Id = BusinessTypeIds.BeautySalon,
                Code = "BEAUTY_SALON",
                Name = "Salão de Beleza",
                IsSystem = true,
                IsActive = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
            },
            new
            {
                Id = BusinessTypeIds.NailStudio,
                Code = "NAIL_STUDIO",
                Name = "Nail Studio",
                IsSystem = true,
                IsActive = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
            },
            new
            {
                Id = BusinessTypeIds.Aesthetics,
                Code = "AESTHETICS",
                Name = "Estética",
                IsSystem = true,
                IsActive = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
            },
            new
            {
                Id = BusinessTypeIds.Massage,
                Code = "MASSAGE",
                Name = "Massagem",
                IsSystem = true,
                IsActive = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
            },
            new
            {
                Id = BusinessTypeIds.PersonalTrainer,
                Code = "PERSONAL_TRAINER",
                Name = "Personal Trainer",
                IsSystem = true,
                IsActive = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
            },
            new
            {
                Id = BusinessTypeIds.HairStylist,
                Code = "HAIR_STYLIST",
                Name = "Cabeleireiro",
                IsSystem = true,
                IsActive = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
            },
            new
            {
                Id = BusinessTypeIds.EyebrowLash,
                Code = "EYEBROW_LASH",
                Name = "Sobrancelhas e Cílios",
                IsSystem = true,
                IsActive = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
            },
            new
            {
                Id = BusinessTypeIds.TattooPiercing,
                Code = "TATTOO_PIERCING",
                Name = "Tatuagem e Piercing",
                IsSystem = true,
                IsActive = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
            },
            new
            {
                Id = BusinessTypeIds.Other,
                Code = "OTHER",
                Name = "Outro",
                IsSystem = true,
                IsActive = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
            });
    }
}