using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualEmployee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                    table.CheckConstraint("CK_customers_phone_not_blank", "length(btrim(phone)) > 0");
                    table.ForeignKey(
                        name: "FK_customers_businesses_tenant_id_business_id",
                        columns: x => new { x.tenant_id, x.business_id },
                        principalTable: "businesses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customers_tenant_id_business_id_phone",
                table: "customers",
                columns: new[] { "tenant_id", "business_id", "phone" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customers");
        }
    }
}
