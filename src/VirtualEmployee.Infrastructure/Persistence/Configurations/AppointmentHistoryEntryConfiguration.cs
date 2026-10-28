using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualEmployee.Domain.Scheduling;

namespace VirtualEmployee.Infrastructure.Persistence.Configurations;

public sealed class AppointmentHistoryEntryConfiguration
    : IEntityTypeConfiguration<AppointmentHistoryEntry>
{
    public void Configure(
        EntityTypeBuilder<AppointmentHistoryEntry> builder)
    {
        builder.ToTable("appointment_history_entries", table =>
        {
            table.HasCheckConstraint(
                "CK_appointment_history_type",
                "type IN (1, 2, 3, 4, 5)");

            table.HasCheckConstraint(
                "CK_appointment_history_before",
                "(type = 1 AND before_state IS NULL) OR " +
                "(type <> 1 AND before_state IS NOT NULL)");

            table.HasCheckConstraint(
                "CK_appointment_history_reason",
                "(type = 2 AND reason IS NOT NULL AND " +
                "length(btrim(reason)) > 0) OR " +
                "(type <> 2 AND reason IS NULL)");

            table.HasCheckConstraint(
                "CK_appointment_history_state_objects",
                "jsonb_typeof(after_state) = 'object' AND " +
                "(before_state IS NULL OR " +
                "jsonb_typeof(before_state) = 'object')");
        });

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(entry => entry.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uuid")
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(entry => entry.AppointmentId)
            .HasColumnName("appointment_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(entry => entry.Type)
            .HasColumnName("type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(entry => entry.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(entry => entry.Before)
            .HasColumnName("before_state")
            .HasColumnType("jsonb")
            .HasConversion(
                state => SerializeState(state!),
                json => DeserializeState(json));

        builder.Property(entry => entry.After)
            .HasColumnName("after_state")
            .HasColumnType("jsonb")
            .HasConversion(
                state => SerializeState(state),
                json => DeserializeState(json))
            .IsRequired();

        builder.Property(entry => entry.Reason)
            .HasColumnName("reason")
            .HasMaxLength(500);

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(entry => new
            {
                entry.TenantId,
                entry.AppointmentId
            })
            .HasPrincipalKey(appointment => new
            {
                appointment.TenantId,
                appointment.Id
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entry => new
        {
            entry.TenantId,
            entry.AppointmentId,
            entry.OccurredAt,
            entry.Id
        });
    }

    private static string SerializeState(
        AppointmentHistoryState state)
    {
        return JsonSerializer.Serialize(state);
    }

    private static AppointmentHistoryState DeserializeState(
        string json)
    {
        return JsonSerializer.Deserialize<AppointmentHistoryState>(json)
            ?? throw new InvalidOperationException(
                "Stored appointment history state could not be read.");
    }
}