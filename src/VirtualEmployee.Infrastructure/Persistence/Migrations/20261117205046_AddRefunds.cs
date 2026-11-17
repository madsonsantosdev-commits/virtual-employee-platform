using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualEmployee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "refunds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    provider_refund_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refunds", x => x.id);
                    table.CheckConstraint("CK_refunds_amount", "amount > 0");
                    table.CheckConstraint("CK_refunds_provider_reference", "(status = 'REFUNDED' AND provider_refund_id IS NOT NULL AND provider_refund_id <> '' AND provider_refund_id !~ '[[:space:][:cntrl:]]') OR (status <> 'REFUNDED' AND provider_refund_id IS NULL)");
                    table.CheckConstraint("CK_refunds_reason", "reason IN ('CUSTOMER_CANCELLATION', 'BUSINESS_CANCELLATION', 'LATE_PAYMENT')");
                    table.CheckConstraint("CK_refunds_status", "status IN ('REFUND_REQUESTED', 'REFUND_PROCESSING', 'REFUNDED', 'REFUND_FAILED')");
                    table.CheckConstraint("CK_refunds_timestamps", "updated_at >= created_at");
                    table.ForeignKey(
                        name: "FK_refunds_payment_attempts_tenant_id_payment_id_payment_attem~",
                        columns: x => new { x.tenant_id, x.payment_id, x.payment_attempt_id },
                        principalTable: "payment_attempts",
                        principalColumns: new[] { "tenant_id", "payment_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_refunds_payments_tenant_id_payment_id_amount",
                        columns: x => new { x.tenant_id, x.payment_id, x.amount },
                        principalTable: "payments",
                        principalColumns: new[] { "tenant_id", "id", "amount" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_refunds_tenant_id_payment_id",
                table: "refunds",
                columns: new[] { "tenant_id", "payment_id" },
                unique: true,
                filter: "status IN ('REFUND_REQUESTED', 'REFUND_PROCESSING', 'REFUNDED')");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_tenant_id_payment_id_amount",
                table: "refunds",
                columns: new[] { "tenant_id", "payment_id", "amount" });

            migrationBuilder.CreateIndex(
                name: "IX_refunds_tenant_id_payment_id_payment_attempt_id",
                table: "refunds",
                columns: new[] { "tenant_id", "payment_id", "payment_attempt_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refunds");
        }
    }
}
