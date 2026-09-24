using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace VirtualEmployee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessTypesAndLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Temporariamente nullable para permitir o backfill
            // de registros existentes.
            migrationBuilder.AddColumn<Guid>(
                name: "business_type_id",
                table: "businesses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                table: "businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "businesses",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "slot_interval_minutes",
                table: "businesses",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "businesses",
                type: "timestamp with time zone",
                nullable: true);

            // Necessária para a FK composta de Location.
            migrationBuilder.AddUniqueConstraint(
                name: "AK_businesses_tenant_id_id",
                table: "businesses",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateTable(
                name: "business_types",
                columns: table => new
                {
                    id = table.Column<Guid>(
                        type: "uuid",
                        nullable: false),

                    code = table.Column<string>(
                        type: "character varying(80)",
                        maxLength: 80,
                        nullable: false),

                    name = table.Column<string>(
                        type: "character varying(120)",
                        maxLength: 120,
                        nullable: false),

                    is_system = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: false),

                    is_active = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: true),

                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false),

                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_business_types",
                        x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    id = table.Column<Guid>(
                        type: "uuid",
                        nullable: false),

                    tenant_id = table.Column<Guid>(
                        type: "uuid",
                        nullable: false),

                    business_id = table.Column<Guid>(
                        type: "uuid",
                        nullable: false),

                    name = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false),

                    phone = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: true),

                    address = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: true),

                    country_code = table.Column<string>(
                        type: "character varying(2)",
                        maxLength: 2,
                        nullable: false),

                    timezone = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false),

                    is_active = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: true),

                    created_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false),

                    updated_at = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_locations",
                        x => x.id);

                    table.ForeignKey(
                        name:
                            "FK_locations_businesses_tenant_id_business_id",
                        columns: x => new
                        {
                            x.tenant_id,
                            x.business_id
                        },
                        principalTable: "businesses",
                        principalColumns: new[]
                        {
                            "tenant_id",
                            "id"
                        },
                        onDelete: ReferentialAction.Restrict);

                    table.ForeignKey(
                        name: "FK_locations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "business_types",
                columns: new[]
                {
                    "id",
                    "code",
                    "created_at",
                    "is_active",
                    "is_system",
                    "name",
                    "updated_at"
                },
                values: new object[,]
                {
                    {
                        new Guid(
                            "11111111-1111-4111-8111-111111111111"),
                        "BARBERSHOP",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero),
                        true,
                        true,
                        "Barbearia",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero)
                    },
                    {
                        new Guid(
                            "22222222-2222-4222-8222-222222222222"),
                        "BEAUTY_SALON",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero),
                        true,
                        true,
                        "Salão de Beleza",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero)
                    },
                    {
                        new Guid(
                            "33333333-3333-4333-8333-333333333333"),
                        "NAIL_STUDIO",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero),
                        true,
                        true,
                        "Nail Studio",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero)
                    },
                    {
                        new Guid(
                            "44444444-4444-4444-8444-444444444444"),
                        "AESTHETICS",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero),
                        true,
                        true,
                        "Estética",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero)
                    },
                    {
                        new Guid(
                            "55555555-5555-4555-8555-555555555555"),
                        "MASSAGE",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero),
                        true,
                        true,
                        "Massagem",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero)
                    },
                    {
                        new Guid(
                            "66666666-6666-4666-8666-666666666666"),
                        "PERSONAL_TRAINER",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero),
                        true,
                        true,
                        "Personal Trainer",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero)
                    },
                    {
                        new Guid(
                            "77777777-7777-4777-8777-777777777777"),
                        "HAIR_STYLIST",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero),
                        true,
                        true,
                        "Cabeleireiro",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero)
                    },
                    {
                        new Guid(
                            "88888888-8888-4888-8888-888888888888"),
                        "EYEBROW_LASH",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero),
                        true,
                        true,
                        "Sobrancelhas e Cílios",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero)
                    },
                    {
                        new Guid(
                            "99999999-9999-4999-8999-999999999999"),
                        "TATTOO_PIERCING",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero),
                        true,
                        true,
                        "Tatuagem e Piercing",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero)
                    },
                    {
                        new Guid(
                            "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
                        "OTHER",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero),
                        true,
                        true,
                        "Outro",
                        new DateTimeOffset(
                            new DateTime(
                                2026, 9, 23, 0, 0, 0,
                                DateTimeKind.Unspecified),
                            TimeSpan.Zero)
                    }
                });

            // Preenche os novos campos para negócios que já existiam
            // antes desta migration.
            migrationBuilder.Sql(
                """
                UPDATE businesses
                SET business_type_id =
                        'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'::uuid,
                    created_at =
                        TIMESTAMPTZ '2026-09-24 00:00:00+00',
                    updated_at =
                        TIMESTAMPTZ '2026-09-24 00:00:00+00'
                WHERE business_type_id IS NULL
                   OR created_at IS NULL
                   OR updated_at IS NULL;
                """);

            // Após o backfill, aplica as restrições definitivas.
            migrationBuilder.AlterColumn<Guid>(
                name: "business_type_id",
                table: "businesses",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "created_at",
                table: "businesses",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "updated_at",
                table: "businesses",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_businesses_business_type_id",
                table: "businesses",
                column: "business_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_business_types_code",
                table: "business_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name:
                    "IX_locations_tenant_id_business_id_is_active",
                table: "locations",
                columns: new[]
                {
                    "tenant_id",
                    "business_id",
                    "is_active"
                });

            migrationBuilder.AddForeignKey(
                name:
                    "FK_businesses_business_types_business_type_id",
                table: "businesses",
                column: "business_type_id",
                principalTable: "business_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name:
                    "FK_businesses_business_types_business_type_id",
                table: "businesses");

            // Locations referencia a chave alternativa de businesses,
            // portanto deve ser removida antes dessa chave.
            migrationBuilder.DropTable(
                name: "locations");

            migrationBuilder.DropIndex(
                name: "IX_businesses_business_type_id",
                table: "businesses");

            migrationBuilder.DropTable(
                name: "business_types");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_businesses_tenant_id_id",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "business_type_id",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "slot_interval_minutes",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "businesses");
        }
    }
}