using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualEmployee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentCompensation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_refunds_tenant_id_payment_id",
                table: "refunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_refunds_reason",
                table: "refunds");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_tenant_id_payment_attempt_id",
                table: "refunds",
                columns: new[] { "tenant_id", "payment_attempt_id" },
                unique: true,
                filter: "status IN ('REFUND_REQUESTED', 'REFUND_PROCESSING', 'REFUNDED')");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_tenant_id_payment_id",
                table: "refunds",
                columns: new[] { "tenant_id", "payment_id" },
                unique: true,
                filter: "reason <> 'ADDITIONAL_PAYMENT' AND status IN ('REFUND_REQUESTED', 'REFUND_PROCESSING', 'REFUNDED')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_refunds_reason",
                table: "refunds",
                sql: "reason IN ('CUSTOMER_CANCELLATION', 'BUSINESS_CANCELLATION', 'LATE_PAYMENT', 'ADDITIONAL_PAYMENT')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_refunds_tenant_id_payment_attempt_id",
                table: "refunds");

            migrationBuilder.DropIndex(
                name: "IX_refunds_tenant_id_payment_id",
                table: "refunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_refunds_reason",
                table: "refunds");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_tenant_id_payment_id",
                table: "refunds",
                columns: new[] { "tenant_id", "payment_id" },
                unique: true,
                filter: "status IN ('REFUND_REQUESTED', 'REFUND_PROCESSING', 'REFUNDED')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_refunds_reason",
                table: "refunds",
                sql: "reason IN ('CUSTOMER_CANCELLATION', 'BUSINESS_CANCELLATION', 'LATE_PAYMENT')");
        }
    }
}
