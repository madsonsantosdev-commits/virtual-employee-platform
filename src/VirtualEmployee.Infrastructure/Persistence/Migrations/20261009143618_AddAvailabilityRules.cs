using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualEmployee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAvailabilityRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "availability_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_availability_rules", x => x.id);
                    table.CheckConstraint("CK_availability_rules_day_of_week", "day_of_week BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_availability_rules_time_window", "start_time < end_time");
                    table.ForeignKey(
                        name: "FK_availability_rules_professional_locations_tenant_id_profess~",
                        columns: x => new { x.tenant_id, x.professional_id, x.location_id },
                        principalTable: "professional_locations",
                        principalColumns: new[] { "tenant_id", "professional_id", "location_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_availability_rules_tenant_id_location_id_professional_id_da~",
                table: "availability_rules",
                columns: new[] { "tenant_id", "location_id", "professional_id", "day_of_week", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_availability_rules_tenant_id_professional_id_location_id",
                table: "availability_rules",
                columns: new[] { "tenant_id", "professional_id", "location_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "availability_rules");
        }
    }
}
