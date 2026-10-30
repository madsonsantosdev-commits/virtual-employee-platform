using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualEmployee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentPaymentConfirmationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_appointment_history_type",
                table: "appointment_history_entries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_appointment_history_type",
                table: "appointment_history_entries",
                sql: "type IN (1, 2, 3, 4, 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_appointment_history_type",
                table: "appointment_history_entries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_appointment_history_type",
                table: "appointment_history_entries",
                sql: "type IN (1, 2, 3, 4)");
        }
    }
}
