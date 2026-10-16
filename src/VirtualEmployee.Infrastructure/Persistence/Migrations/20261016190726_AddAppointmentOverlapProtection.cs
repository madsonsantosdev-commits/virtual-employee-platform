using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualEmployee.Infrastructure.Persistence.Migrations
{
    public partial class AddAppointmentOverlapProtection : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE EXTENSION IF NOT EXISTS btree_gist;

                ALTER TABLE appointments
                ADD CONSTRAINT ex_appointments_no_overlap
                EXCLUDE USING gist (
                    tenant_id WITH =,
                    professional_id WITH =,
                    tstzrange(starts_at, ends_at, '[)') WITH &&
                )
                WHERE (
                    status IN (
                        'PENDING',
                        'CONFIRMED',
                        'CONFIRMED_BY_CLIENT',
                        'RESCHEDULE_REQUESTED'
                    )
                );
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE appointments
                DROP CONSTRAINT ex_appointments_no_overlap;
                """);
        }
    }
}