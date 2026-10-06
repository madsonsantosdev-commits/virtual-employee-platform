using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualEmployee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfessionalLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_professionals_tenant_id_id",
                table: "professionals",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateTable(
                name: "professional_locations",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_professional_locations", x => new { x.tenant_id, x.professional_id, x.location_id });
                    table.ForeignKey(
                        name: "FK_professional_locations_locations_tenant_id_location_id",
                        columns: x => new { x.tenant_id, x.location_id },
                        principalTable: "locations",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_professional_locations_professionals_tenant_id_professional~",
                        columns: x => new { x.tenant_id, x.professional_id },
                        principalTable: "professionals",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_professional_locations_tenant_id_location_id_is_active",
                table: "professional_locations",
                columns: new[] { "tenant_id", "location_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_professional_locations_tenant_id_professional_id_is_active",
                table: "professional_locations",
                columns: new[] { "tenant_id", "professional_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "professional_locations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_professionals_tenant_id_id",
                table: "professionals");
        }
    }
}
