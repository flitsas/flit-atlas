using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Soporte.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class Recordatorios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "hora_recordatorios",
                schema: "atlas",
                table: "soporte_configuracion",
                type: "time without time zone",
                nullable: false,
                // La fila que ya exista queda con la hora por defecto (07:00, Bogotá).
                defaultValue: new TimeOnly(7, 0, 0));

            migrationBuilder.CreateTable(
                name: "soporte_recordatorio",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    momento = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    canal = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    equipo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    turno_id = table.Column<Guid>(type: "uuid", nullable: true),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    enviado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soporte_recordatorio", x => x.id);
                    table.ForeignKey(
                        name: "fk_soporte_recordatorio_soporte_equipo_equipo_id",
                        column: x => x.equipo_id,
                        principalSchema: "atlas",
                        principalTable: "soporte_equipo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_soporte_recordatorio_soporte_turno_turno_id",
                        column: x => x.turno_id,
                        principalSchema: "atlas",
                        principalTable: "soporte_turno",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Recordatorios de turno enviados por canal (HU 2.11): impiden mandar dos veces el mismo.");

            migrationBuilder.CreateTable(
                name: "soporte_recordatorio_corrida",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    disparo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    pedida_por_persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    terminada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    enviados = table.Column<int>(type: "integer", nullable: false),
                    fallidos = table.Column<int>(type: "integer", nullable: false),
                    omitidos = table.Column<int>(type: "integer", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soporte_recordatorio_corrida", x => x.id);
                },
                comment: "Corridas del trabajo diario de recordatorios (HU 2.11), con sus conteos.");

            migrationBuilder.CreateIndex(
                name: "ix_soporte_recordatorio_equipo_id",
                schema: "atlas",
                table: "soporte_recordatorio",
                column: "equipo_id");

            migrationBuilder.CreateIndex(
                name: "ix_soporte_recordatorio_fecha_estado",
                schema: "atlas",
                table: "soporte_recordatorio",
                columns: new[] { "fecha", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_soporte_recordatorio_turno_id",
                schema: "atlas",
                table: "soporte_recordatorio",
                column: "turno_id");

            migrationBuilder.CreateIndex(
                name: "ux_soporte_recordatorio_clave",
                schema: "atlas",
                table: "soporte_recordatorio",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_soporte_recordatorio_corrida_programada",
                schema: "atlas",
                table: "soporte_recordatorio_corrida",
                column: "fecha",
                unique: true,
                filter: "disparo = 'programada'");

            // Llaves hacia el núcleo, a mano: el contexto del módulo no mapea plataforma_persona (ATLAS-ADR-0029).
            migrationBuilder.Sql("""
                ALTER TABLE atlas.soporte_recordatorio ADD CONSTRAINT fk_soporte_recordatorio_plataforma_persona
                    FOREIGN KEY (persona_id) REFERENCES atlas.plataforma_persona (id) ON DELETE RESTRICT;
                CREATE INDEX ix_soporte_recordatorio_persona_id ON atlas.soporte_recordatorio (persona_id);
                ALTER TABLE atlas.soporte_recordatorio_corrida ADD CONSTRAINT fk_soporte_recordatorio_corrida_plataforma_persona
                    FOREIGN KEY (pedida_por_persona_id) REFERENCES atlas.plataforma_persona (id) ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "soporte_recordatorio",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "soporte_recordatorio_corrida",
                schema: "atlas");

            migrationBuilder.DropColumn(
                name: "hora_recordatorios",
                schema: "atlas",
                table: "soporte_configuracion");
        }
    }
}
