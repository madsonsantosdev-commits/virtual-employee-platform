using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualEmployee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServicesAndCombos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    service_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_services", x => x.id);
                    table.UniqueConstraint("AK_services_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "FK_services_businesses_tenant_id_business_id",
                        columns: x => new { x.tenant_id, x.business_id },
                        principalTable: "businesses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_services_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_components",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    combo_service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_components", x => new { x.tenant_id, x.combo_service_id, x.component_service_id });
                    table.ForeignKey(
                        name: "FK_service_components_services_tenant_id_combo_service_id",
                        columns: x => new { x.tenant_id, x.combo_service_id },
                        principalTable: "services",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_service_components_services_tenant_id_component_service_id",
                        columns: x => new { x.tenant_id, x.component_service_id },
                        principalTable: "services",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_service_components_tenant_id_combo_service_id_sort_order",
                table: "service_components",
                columns: new[] { "tenant_id", "combo_service_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_service_components_tenant_id_component_service_id",
                table: "service_components",
                columns: new[] { "tenant_id", "component_service_id" });

            migrationBuilder.CreateIndex(
                name: "IX_services_tenant_id_business_id_is_active",
                table: "services",
                columns: new[] { "tenant_id", "business_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_components");

            migrationBuilder.DropTable(
                name: "services");
        }
    }
}
