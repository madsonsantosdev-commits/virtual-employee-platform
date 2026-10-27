using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualEmployee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentsAndPaymentAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_appointments_tenant_id_business_id",
                table: "appointments");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_appointments_tenant_id_business_id_id",
                table: "appointments",
                columns: new[] { "tenant_id", "business_id", "id" });

            migrationBuilder.CreateTable(
                name: "payment_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider_payment_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_attempts", x => x.id);
                    table.UniqueConstraint("AK_payment_attempts_tenant_id_payment_id_id", x => new { x.tenant_id, x.payment_id, x.id });
                    table.CheckConstraint("CK_payment_attempts_amount", "amount > 0");
                    table.CheckConstraint("CK_payment_attempts_confirmation", "(status = 'CONFIRMED' AND confirmed_at IS NOT NULL AND confirmed_at >= created_at AND confirmed_at <= updated_at) OR (status <> 'CONFIRMED' AND confirmed_at IS NULL)");
                    table.CheckConstraint("CK_payment_attempts_expiration", "status <> 'EXPIRED' OR updated_at >= expires_at");
                    table.CheckConstraint("CK_payment_attempts_method", "payment_method IN ('PIX', 'CREDIT_CARD', 'DEBIT_CARD')");
                    table.CheckConstraint("CK_payment_attempts_provider_pair", "(provider IS NULL AND provider_payment_id IS NULL) OR (provider IS NOT NULL AND provider_payment_id IS NOT NULL)");
                    table.CheckConstraint("CK_payment_attempts_provider_references", "(provider IS NULL OR (length(provider) > 0 AND provider !~ '[[:space:][:cntrl:]]')) AND (provider_payment_id IS NULL OR (length(provider_payment_id) > 0 AND provider_payment_id !~ '[[:space:][:cntrl:]]'))");
                    table.CheckConstraint("CK_payment_attempts_provider_state", "(status <> 'CREATED' OR provider IS NULL) AND (status NOT IN ('PENDING', 'PROCESSING', 'CONFIRMED') OR provider IS NOT NULL)");
                    table.CheckConstraint("CK_payment_attempts_status", "status IN ('CREATED', 'PENDING', 'PROCESSING', 'CONFIRMED', 'FAILED', 'EXPIRED', 'CANCELLED')");
                    table.CheckConstraint("CK_payment_attempts_timestamps", "updated_at >= created_at AND expires_at > created_at");
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_payment_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.id);
                    table.UniqueConstraint("AK_payments_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.UniqueConstraint("AK_payments_tenant_id_id_amount", x => new { x.tenant_id, x.id, x.amount });
                    table.CheckConstraint("CK_payments_amount", "amount > 0");
                    table.CheckConstraint("CK_payments_confirmation", "(status IN ('CONFIRMED', 'REFUNDED') AND confirmed_payment_attempt_id IS NOT NULL AND confirmed_at IS NOT NULL AND confirmed_at >= created_at AND confirmed_at <= updated_at) OR (status NOT IN ('CONFIRMED', 'REFUNDED') AND confirmed_payment_attempt_id IS NULL AND confirmed_at IS NULL)");
                    table.CheckConstraint("CK_payments_status", "status IN ('PENDING', 'PROCESSING', 'CONFIRMED', 'FAILED', 'CANCELLED', 'REFUNDED')");
                    table.CheckConstraint("CK_payments_timestamps", "updated_at >= created_at");
                    table.ForeignKey(
                        name: "FK_payments_appointments_tenant_id_business_id_appointment_id",
                        columns: x => new { x.tenant_id, x.business_id, x.appointment_id },
                        principalTable: "appointments",
                        principalColumns: new[] { "tenant_id", "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payments_payment_attempts_tenant_id_id_confirmed_payment_at~",
                        columns: x => new { x.tenant_id, x.id, x.confirmed_payment_attempt_id },
                        principalTable: "payment_attempts",
                        principalColumns: new[] { "tenant_id", "payment_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_attempts_provider_provider_payment_id",
                table: "payment_attempts",
                columns: new[] { "provider", "provider_payment_id" },
                unique: true,
                filter: "provider_payment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payment_attempts_tenant_id_payment_id_amount",
                table: "payment_attempts",
                columns: new[] { "tenant_id", "payment_id", "amount" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_attempts_tenant_id_payment_id_created_at",
                table: "payment_attempts",
                columns: new[] { "tenant_id", "payment_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_attempts_tenant_id_status_expires_at",
                table: "payment_attempts",
                columns: new[] { "tenant_id", "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_tenant_id_appointment_id",
                table: "payments",
                columns: new[] { "tenant_id", "appointment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_tenant_id_business_id",
                table: "payments",
                columns: new[] { "tenant_id", "business_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_tenant_id_business_id_appointment_id",
                table: "payments",
                columns: new[] { "tenant_id", "business_id", "appointment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_tenant_id_id_confirmed_payment_attempt_id",
                table: "payments",
                columns: new[] { "tenant_id", "id", "confirmed_payment_attempt_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_payment_attempts_payments_tenant_id_payment_id_amount",
                table: "payment_attempts",
                columns: new[] { "tenant_id", "payment_id", "amount" },
                principalTable: "payments",
                principalColumns: new[] { "tenant_id", "id", "amount" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payment_attempts_payments_tenant_id_payment_id_amount",
                table: "payment_attempts");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "payment_attempts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_appointments_tenant_id_business_id_id",
                table: "appointments");

            migrationBuilder.CreateIndex(
                name: "IX_appointments_tenant_id_business_id",
                table: "appointments",
                columns: new[] { "tenant_id", "business_id" });
        }
    }
}
