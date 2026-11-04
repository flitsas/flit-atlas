using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Plataforma.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class SincronizacionPersonas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "foto_hash",
                schema: "atlas",
                table: "plataforma_persona",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "inactiva_por_entra",
                schema: "atlas",
                table: "plataforma_persona",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sincronizada_en",
                schema: "atlas",
                table: "plataforma_persona",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "plataforma_persona_foto",
                schema: "atlas",
                columns: table => new
                {
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contenido = table.Column<byte[]>(type: "bytea", nullable: false, comment: "@pii"),
                    tipo_contenido = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actualizada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_persona_foto", x => x.persona_id);
                    table.ForeignKey(
                        name: "fk_plataforma_persona_foto_plataforma_persona_persona_id",
                        column: x => x.persona_id,
                        principalSchema: "atlas",
                        principalTable: "plataforma_persona",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Foto de perfil de los empleados leída de Graph (ATLAS-ADR-0030).");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plataforma_persona_foto",
                schema: "atlas");

            migrationBuilder.DropColumn(
                name: "foto_hash",
                schema: "atlas",
                table: "plataforma_persona");

            migrationBuilder.DropColumn(
                name: "inactiva_por_entra",
                schema: "atlas",
                table: "plataforma_persona");

            migrationBuilder.DropColumn(
                name: "sincronizada_en",
                schema: "atlas",
                table: "plataforma_persona");
        }
    }
}
