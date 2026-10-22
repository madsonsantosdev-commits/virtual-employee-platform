using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualEmployee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appointment_history_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    before_state = table.Column<string>(type: "jsonb", nullable: true),
                    after_state = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointment_history_entries", x => x.id);
                    table.CheckConstraint("CK_appointment_history_before", "(type = 1 AND before_state IS NULL) OR (type <> 1 AND before_state IS NOT NULL)");
                    table.CheckConstraint("CK_appointment_history_reason", "(type = 2 AND reason IS NOT NULL AND length(btrim(reason)) > 0) OR (type <> 2 AND reason IS NULL)");
                    table.CheckConstraint("CK_appointment_history_state_objects", "jsonb_typeof(after_state) = 'object' AND (before_state IS NULL OR jsonb_typeof(before_state) = 'object')");
                    table.CheckConstraint("CK_appointment_history_type", "type IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_appointment_history_entries_appointments_tenant_id_appointm~",
                        columns: x => new { x.tenant_id, x.appointment_id },
                        principalTable: "appointments",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_appointment_history_entries_tenant_id_appointment_id_occurr~",
                table: "appointment_history_entries",
                columns: new[] { "tenant_id", "appointment_id", "occurred_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointment_history_entries");
        }
    }
}
