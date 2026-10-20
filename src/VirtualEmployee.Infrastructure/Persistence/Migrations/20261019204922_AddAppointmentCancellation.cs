using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualEmployee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                table: "appointments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                table: "appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_appointments_cancellation",
                table: "appointments",
                sql: "(status IN ('CANCELLED_BY_CLIENT', 'CANCELLED_BY_BUSINESS') AND cancellation_reason IS NOT NULL AND length(btrim(cancellation_reason)) > 0 AND cancelled_at IS NOT NULL AND cancelled_at >= created_at AND cancelled_at <= updated_at) OR (status NOT IN ('CANCELLED_BY_CLIENT', 'CANCELLED_BY_BUSINESS') AND cancellation_reason IS NULL AND cancelled_at IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_appointments_cancellation",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "appointments");
        }
    }
}
