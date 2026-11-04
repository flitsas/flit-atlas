using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Soporte.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class CambiosEImportacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_soporte_compensacion_turno_id",
                schema: "atlas",
                table: "soporte_compensacion");

            migrationBuilder.AlterTable(
                name: "soporte_compensacion",
                schema: "atlas",
                comment: "Días compensados por turnos: a quién, qué día, quién aprobó y si ya se tomó (HU 2.9).",
                oldComment: "Días compensados por turnos; el flujo llega en la HU 2.9.");

            migrationBuilder.CreateTable(
                name: "soporte_solicitud_cambio",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    turno_id = table.Column<Guid>(type: "uuid", nullable: false),
                    equipo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    solicitante_persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reemplazo_persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "@pii"),
                    turno_nuevo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelta_por_persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelta_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soporte_solicitud_cambio", x => x.id);
                    table.CheckConstraint("ck_soporte_solicitud_cambio_personas", "solicitante_persona_id <> reemplazo_persona_id");
                    table.ForeignKey(
                        name: "fk_soporte_solicitud_cambio_soporte_equipo_equipo_id",
                        column: x => x.equipo_id,
                        principalSchema: "atlas",
                        principalTable: "soporte_equipo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_soporte_solicitud_cambio_soporte_turno_turno_id",
                        column: x => x.turno_id,
                        principalSchema: "atlas",
                        principalTable: "soporte_turno",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_soporte_solicitud_cambio_soporte_turno_turno_nuevo_id",
                        column: x => x.turno_nuevo_id,
                        principalSchema: "atlas",
                        principalTable: "soporte_turno",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Solicitudes de cambio de turno: solicitud, aceptación del reemplazo y aprobación (HU 2.9).");

            migrationBuilder.CreateTable(
                name: "soporte_solicitud_evento",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitud_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accion = table.Column<string>(type: "character varying(23)", maxLength: 23, nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "@pii"),
                    llave_idempotencia = table.Column<Guid>(type: "uuid", nullable: false),
                    hecha_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soporte_solicitud_evento", x => x.id);
                    table.ForeignKey(
                        name: "fk_soporte_solicitud_evento_soporte_solicitud_cambio_solicitud",
                        column: x => x.solicitud_id,
                        principalSchema: "atlas",
                        principalTable: "soporte_solicitud_cambio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Historial de cada solicitud de cambio, con la llave de idempotencia de cada acción.");

            migrationBuilder.CreateIndex(
                name: "ix_soporte_compensacion_turno_id_persona_id",
                schema: "atlas",
                table: "soporte_compensacion",
                columns: new[] { "turno_id", "persona_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_soporte_compensacion_tomada",
                schema: "atlas",
                table: "soporte_compensacion",
                sql: "estado <> 'tomada' OR fecha_compensada IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_soporte_solicitud_cambio_equipo_id_estado",
                schema: "atlas",
                table: "soporte_solicitud_cambio",
                columns: new[] { "equipo_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_soporte_solicitud_cambio_reemplazo_persona_id",
                schema: "atlas",
                table: "soporte_solicitud_cambio",
                column: "reemplazo_persona_id");

            migrationBuilder.CreateIndex(
                name: "ix_soporte_solicitud_cambio_solicitante_persona_id",
                schema: "atlas",
                table: "soporte_solicitud_cambio",
                column: "solicitante_persona_id");

            migrationBuilder.CreateIndex(
                name: "ix_soporte_solicitud_cambio_turno_nuevo_id",
                schema: "atlas",
                table: "soporte_solicitud_cambio",
                column: "turno_nuevo_id");

            migrationBuilder.CreateIndex(
                name: "ux_soporte_solicitud_cambio_abierta",
                schema: "atlas",
                table: "soporte_solicitud_cambio",
                column: "turno_id",
                unique: true,
                filter: "estado IN ('pendiente_reemplazo', 'pendiente_aprobacion')");

            migrationBuilder.CreateIndex(
                name: "ix_soporte_solicitud_evento_solicitud_id",
                schema: "atlas",
                table: "soporte_solicitud_evento",
                column: "solicitud_id");

            migrationBuilder.CreateIndex(
                name: "ux_soporte_solicitud_evento_llave",
                schema: "atlas",
                table: "soporte_solicitud_evento",
                column: "llave_idempotencia",
                unique: true);

            // A mano (como en la migración inicial): las llaves foráneas hacia las personas del núcleo.
            migrationBuilder.Sql("""
                ALTER TABLE atlas.soporte_solicitud_cambio ADD CONSTRAINT fk_soporte_solicitud_cambio_plataforma_persona_solicitante
                    FOREIGN KEY (solicitante_persona_id) REFERENCES atlas.plataforma_persona (id) ON DELETE RESTRICT;
                ALTER TABLE atlas.soporte_solicitud_cambio ADD CONSTRAINT fk_soporte_solicitud_cambio_plataforma_persona_reemplazo
                    FOREIGN KEY (reemplazo_persona_id) REFERENCES atlas.plataforma_persona (id) ON DELETE RESTRICT;
                ALTER TABLE atlas.soporte_solicitud_evento ADD CONSTRAINT fk_soporte_solicitud_evento_plataforma_persona_persona_id
                    FOREIGN KEY (persona_id) REFERENCES atlas.plataforma_persona (id) ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "soporte_solicitud_evento",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "soporte_solicitud_cambio",
                schema: "atlas");

            migrationBuilder.DropIndex(
                name: "ix_soporte_compensacion_turno_id_persona_id",
                schema: "atlas",
                table: "soporte_compensacion");

            migrationBuilder.DropCheckConstraint(
                name: "ck_soporte_compensacion_tomada",
                schema: "atlas",
                table: "soporte_compensacion");

            migrationBuilder.AlterTable(
                name: "soporte_compensacion",
                schema: "atlas",
                comment: "Días compensados por turnos; el flujo llega en la HU 2.9.",
                oldComment: "Días compensados por turnos: a quién, qué día, quién aprobó y si ya se tomó (HU 2.9).");

            migrationBuilder.CreateIndex(
                name: "ix_soporte_compensacion_turno_id",
                schema: "atlas",
                table: "soporte_compensacion",
                column: "turno_id");
        }
    }
}
