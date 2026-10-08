using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualEmployee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfessionalServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "professional_services",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_professional_services", x => new { x.tenant_id, x.professional_id, x.service_id });
                    table.ForeignKey(
                        name: "FK_professional_services_professionals_tenant_id_professional_~",
                        columns: x => new { x.tenant_id, x.professional_id },
                        principalTable: "professionals",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_professional_services_services_tenant_id_service_id",
                        columns: x => new { x.tenant_id, x.service_id },
                        principalTable: "services",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_professional_services_tenant_id_professional_id_is_active",
                table: "professional_services",
                columns: new[] { "tenant_id", "professional_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_professional_services_tenant_id_service_id",
                table: "professional_services",
                columns: new[] { "tenant_id", "service_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "professional_services");
        }
    }
}
