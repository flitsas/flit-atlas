using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Flit.Atlas.Modulos.Soporte.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "atlas");

            migrationBuilder.CreateTable(
                name: "soporte_ausencia",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    desde = table.Column<DateOnly>(type: "date", nullable: false),
                    hasta = table.Column<DateOnly>(type: "date", nullable: false),
                    tipo = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false, comment: "@pii"),
                    registrada_por_persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soporte_ausencia", x => x.id);
                    table.CheckConstraint("ck_soporte_ausencia_rango", "hasta >= desde");
                },
                comment: "Ausencias por persona y rango (vacaciones, incapacidades); la generación las salta.");

            migrationBuilder.CreateTable(
                name: "soporte_configuracion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    unica = table.Column<bool>(type: "boolean", nullable: false),
                    hora_inicio_por_defecto = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    hora_fin_por_defecto = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    regla_festivos = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    dias_anticipacion_recordatorio = table.Column<int>(type: "integer", nullable: true),
                    canal_externo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "@pii"),
                    actualizado_por_persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soporte_configuracion", x => x.id);
                    table.CheckConstraint("ck_soporte_configuracion_horario", "hora_fin_por_defecto > hora_inicio_por_defecto");
                    table.CheckConstraint("ck_soporte_configuracion_unica", "unica");
                },
                comment: "Configuración global del soporte de los sábados (una fila).");

            migrationBuilder.CreateTable(
                name: "soporte_equipo",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    personas_minimo = table.Column<int>(type: "integer", nullable: false),
                    personas_maximo = table.Column<int>(type: "integer", nullable: false),
                    hora_inicio = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    hora_fin = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soporte_equipo", x => x.id);
                    table.CheckConstraint("ck_soporte_equipo_horario", "(hora_inicio IS NULL AND hora_fin IS NULL) OR hora_fin > hora_inicio");
                    table.CheckConstraint("ck_soporte_equipo_personas", "personas_minimo >= 1 AND personas_maximo >= personas_minimo AND personas_maximo <= 10");
                },
                comment: "Equipos de soporte de los sábados por área, con cuántas personas van por sábado.");

            migrationBuilder.CreateTable(
                name: "soporte_festivo",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soporte_festivo", x => x.id);
                },
                comment: "Días no laborables adicionales; los festivos de ley se calculan (Ley 51 de 1983 y Ley 2578 de 2026).");

            migrationBuilder.CreateTable(
                name: "soporte_participante",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    equipo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soporte_participante", x => x.id);
                    table.ForeignKey(
                        name: "fk_soporte_participante_soporte_equipo_equipo_id",
                        column: x => x.equipo_id,
                        principalSchema: "atlas",
                        principalTable: "soporte_equipo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Personas internas de la rotación de cada equipo, con su orden y estado.");

            migrationBuilder.CreateTable(
                name: "soporte_turno",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    equipo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hora_inicio = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    hora_fin = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    estado = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    origen = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    turno_reemplazado_id = table.Column<Guid>(type: "uuid", nullable: true),
                    generacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    observacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "@pii"),
                    requiere_revision = table.Column<bool>(type: "boolean", nullable: false),
                    creado_por_persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soporte_turno", x => x.id);
                    table.CheckConstraint("ck_soporte_turno_horario", "hora_fin IS NULL OR hora_inicio IS NULL OR hora_fin > hora_inicio");
                    table.ForeignKey(
                        name: "fk_soporte_turno_soporte_equipo_equipo_id",
                        column: x => x.equipo_id,
                        principalSchema: "atlas",
                        principalTable: "soporte_equipo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_soporte_turno_soporte_turno_turno_reemplazado_id",
                        column: x => x.turno_reemplazado_id,
                        principalSchema: "atlas",
                        principalTable: "soporte_turno",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Turnos de soporte de los sábados: una persona de un equipo en una fecha.");

            migrationBuilder.CreateTable(
                name: "soporte_compensacion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    turno_id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_compensada = table.Column<DateOnly>(type: "date", nullable: true),
                    estado = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    aprobada_por_persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "@pii"),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soporte_compensacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_soporte_compensacion_soporte_turno_turno_id",
                        column: x => x.turno_id,
                        principalSchema: "atlas",
                        principalTable: "soporte_turno",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Días compensados por turnos; el flujo llega en la HU 2.9.");

            migrationBuilder.InsertData(
                schema: "atlas",
                table: "soporte_equipo",
                columns: new[] { "id", "activo", "actualizado_en", "area", "creado_en", "hora_fin", "hora_inicio", "nombre", "personas_maximo", "personas_minimo" },
                values: new object[,]
                {
                    { new Guid("0199a0c0-0000-7000-8000-000000000001"), true, new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TI", new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "Desarrollo TI", 1, 1 },
                    { new Guid("0199a0c0-0000-7000-8000-000000000002"), true, new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Operaciones", new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "Operaciones", 2, 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_soporte_ausencia_persona_id_desde",
                schema: "atlas",
                table: "soporte_ausencia",
                columns: new[] { "persona_id", "desde" });

            migrationBuilder.CreateIndex(
                name: "ix_soporte_compensacion_persona_id_estado",
                schema: "atlas",
                table: "soporte_compensacion",
                columns: new[] { "persona_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_soporte_compensacion_turno_id",
                schema: "atlas",
                table: "soporte_compensacion",
                column: "turno_id");

            migrationBuilder.CreateIndex(
                name: "ix_soporte_configuracion_unica",
                schema: "atlas",
                table: "soporte_configuracion",
                column: "unica",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_soporte_equipo_nombre",
                schema: "atlas",
                table: "soporte_equipo",
                column: "nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_soporte_festivo_fecha",
                schema: "atlas",
                table: "soporte_festivo",
                column: "fecha",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_soporte_participante_equipo_id_persona_id",
                schema: "atlas",
                table: "soporte_participante",
                columns: new[] { "equipo_id", "persona_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_soporte_turno_fecha_equipo_id",
                schema: "atlas",
                table: "soporte_turno",
                columns: new[] { "fecha", "equipo_id" });

            migrationBuilder.CreateIndex(
                name: "ix_soporte_turno_persona_id_fecha",
                schema: "atlas",
                table: "soporte_turno",
                columns: new[] { "persona_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_soporte_turno_turno_reemplazado_id",
                schema: "atlas",
                table: "soporte_turno",
                column: "turno_reemplazado_id");

            migrationBuilder.CreateIndex(
                name: "ux_soporte_turno_vigente",
                schema: "atlas",
                table: "soporte_turno",
                columns: new[] { "equipo_id", "fecha", "persona_id" },
                unique: true,
                filter: "estado IN ('programado', 'confirmado', 'aplicado')");

            // A mano (ATLAS-ADR-0026 y ATLAS-ADR-0029): las llaves foráneas hacia las personas del núcleo, que el contexto
            // del módulo no mapea. El núcleo se migra antes que los módulos. Ninguna tabla de soporte tiene
            // organizacion_id (la rotación es de empleados de FLIT), así que no llevan RLS.
            migrationBuilder.Sql("""
                ALTER TABLE atlas.soporte_participante ADD CONSTRAINT fk_soporte_participante_plataforma_persona_persona_id
                    FOREIGN KEY (persona_id) REFERENCES atlas.plataforma_persona (id) ON DELETE RESTRICT;
                ALTER TABLE atlas.soporte_turno ADD CONSTRAINT fk_soporte_turno_plataforma_persona_persona_id
                    FOREIGN KEY (persona_id) REFERENCES atlas.plataforma_persona (id) ON DELETE RESTRICT;
                ALTER TABLE atlas.soporte_ausencia ADD CONSTRAINT fk_soporte_ausencia_plataforma_persona_persona_id
                    FOREIGN KEY (persona_id) REFERENCES atlas.plataforma_persona (id) ON DELETE RESTRICT;
                ALTER TABLE atlas.soporte_compensacion ADD CONSTRAINT fk_soporte_compensacion_plataforma_persona_persona_id
                    FOREIGN KEY (persona_id) REFERENCES atlas.plataforma_persona (id) ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "soporte_ausencia",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "soporte_compensacion",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "soporte_configuracion",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "soporte_festivo",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "soporte_participante",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "soporte_turno",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "soporte_equipo",
                schema: "atlas");
        }
    }
}
