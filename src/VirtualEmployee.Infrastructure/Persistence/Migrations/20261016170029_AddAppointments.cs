using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualEmployee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_customers_tenant_id_id",
                table: "customers",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateTable(
                name: "appointments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total_price_snapshot = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    total_duration_minutes_snapshot = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reservation_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointments", x => x.id);
                    table.UniqueConstraint("AK_appointments_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("CK_appointments_duration_matches_window", "ends_at = starts_at + total_duration_minutes_snapshot * interval '1 minute'");
                    table.CheckConstraint("CK_appointments_pending_expiration", "status <> 'PENDING' OR (reservation_expires_at IS NOT NULL AND reservation_expires_at > created_at AND reservation_expires_at <= starts_at)");
                    table.CheckConstraint("CK_appointments_status", "status IN ('PENDING', 'CONFIRMED', 'CONFIRMED_BY_CLIENT', 'RESCHEDULE_REQUESTED', 'CANCELLED_BY_CLIENT', 'CANCELLED_BY_BUSINESS', 'COMPLETED', 'NO_SHOW', 'EXPIRED')");
                    table.CheckConstraint("CK_appointments_time_window", "starts_at < ends_at");
                    table.CheckConstraint("CK_appointments_total_duration", "total_duration_minutes_snapshot > 0");
                    table.CheckConstraint("CK_appointments_total_price", "total_price_snapshot >= 0");
                    table.ForeignKey(
                        name: "FK_appointments_businesses_tenant_id_business_id",
                        columns: x => new { x.tenant_id, x.business_id },
                        principalTable: "businesses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_appointments_customers_tenant_id_customer_id",
                        columns: x => new { x.tenant_id, x.customer_id },
                        principalTable: "customers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_appointments_professional_locations_tenant_id_professional_~",
                        columns: x => new { x.tenant_id, x.professional_id, x.location_id },
                        principalTable: "professional_locations",
                        principalColumns: new[] { "tenant_id", "professional_id", "location_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointment_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name_snapshot = table.Column<string>(type: "text", nullable: false),
                    price_snapshot = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    duration_minutes_snapshot = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointment_items", x => x.id);
                    table.CheckConstraint("CK_appointment_items_duration", "duration_minutes_snapshot > 0");
                    table.CheckConstraint("CK_appointment_items_price", "price_snapshot >= 0");
                    table.CheckConstraint("CK_appointment_items_service_name", "length(btrim(service_name_snapshot)) > 0");
                    table.ForeignKey(
                        name: "FK_appointment_items_appointments_tenant_id_appointment_id",
                        columns: x => new { x.tenant_id, x.appointment_id },
                        principalTable: "appointments",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_appointment_items_services_tenant_id_service_id",
                        columns: x => new { x.tenant_id, x.service_id },
                        principalTable: "services",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_appointment_items_tenant_id_appointment_id_service_id",
                table: "appointment_items",
                columns: new[] { "tenant_id", "appointment_id", "service_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_appointment_items_tenant_id_service_id",
                table: "appointment_items",
                columns: new[] { "tenant_id", "service_id" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_tenant_id_business_id",
                table: "appointments",
                columns: new[] { "tenant_id", "business_id" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_tenant_id_customer_id_starts_at",
                table: "appointments",
                columns: new[] { "tenant_id", "customer_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_tenant_id_location_id_starts_at",
                table: "appointments",
                columns: new[] { "tenant_id", "location_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_tenant_id_professional_id_location_id",
                table: "appointments",
                columns: new[] { "tenant_id", "professional_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_tenant_id_professional_id_starts_at",
                table: "appointments",
                columns: new[] { "tenant_id", "professional_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_tenant_id_status_reservation_expires_at",
                table: "appointments",
                columns: new[] { "tenant_id", "status", "reservation_expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointment_items");

            migrationBuilder.DropTable(
                name: "appointments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_customers_tenant_id_id",
                table: "customers");
        }
    }
}
