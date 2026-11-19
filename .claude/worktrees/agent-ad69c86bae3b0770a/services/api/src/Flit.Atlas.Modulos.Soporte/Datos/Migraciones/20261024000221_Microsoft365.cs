using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Soporte.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class Microsoft365 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "soporte_aviso_teams",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    canal = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    contenido = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false, comment: "@pii"),
                    pendiente = table.Column<bool>(type: "boolean", nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    proximo_intento_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    enviado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soporte_aviso_teams", x => x.id);
                },
                comment: "Avisos por publicar en un canal de Teams, con reintentos (HU 2.10 y 2.11).");

            migrationBuilder.CreateTable(
                name: "soporte_evento_calendario",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    turno_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_graph_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    persona_sincronizada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pendiente = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    proximo_intento_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultimo_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    sincronizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soporte_evento_calendario", x => x.id);
                },
                comment: "Evento de Outlook de cada turno en el calendario compartido (id de Graph) y su sincronización pendiente (HU 2.10).");

            migrationBuilder.CreateTable(
                name: "soporte_feed_ics",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    equipo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    revocado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_uso_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soporte_feed_ics", x => x.id);
                    table.CheckConstraint("ck_soporte_feed_ics_equipo", "(tipo = 'equipo') = (equipo_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_soporte_feed_ics_soporte_equipo_equipo_id",
                        column: x => x.equipo_id,
                        principalSchema: "atlas",
                        principalTable: "soporte_equipo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Feeds .ics personales y de equipo; el token solo se guarda como hash SHA-256 (HU 2.10).");

            migrationBuilder.CreateIndex(
                name: "ix_soporte_aviso_teams_pendientes",
                schema: "atlas",
                table: "soporte_aviso_teams",
                column: "proximo_intento_en",
                filter: "pendiente");

            migrationBuilder.CreateIndex(
                name: "ix_soporte_evento_calendario_pendientes",
                schema: "atlas",
                table: "soporte_evento_calendario",
                column: "proximo_intento_en",
                filter: "pendiente");

            migrationBuilder.CreateIndex(
                name: "ix_soporte_evento_calendario_turno_id",
                schema: "atlas",
                table: "soporte_evento_calendario",
                column: "turno_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_soporte_feed_ics_equipo_id",
                schema: "atlas",
                table: "soporte_feed_ics",
                column: "equipo_id");

            migrationBuilder.CreateIndex(
                name: "ix_soporte_feed_ics_token_hash",
                schema: "atlas",
                table: "soporte_feed_ics",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_soporte_feed_ics_vigente",
                schema: "atlas",
                table: "soporte_feed_ics",
                columns: new[] { "persona_id", "tipo", "equipo_id" },
                unique: true,
                filter: "revocado_en IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            // A mano (como en las migraciones anteriores): la llave foránea hacia las personas del núcleo. El evento de
            // calendario no la tiene hacia el turno a propósito: un turno borrado debe poder cancelar su evento.
            migrationBuilder.Sql("""
                ALTER TABLE atlas.soporte_feed_ics ADD CONSTRAINT fk_soporte_feed_ics_plataforma_persona_persona_id
                    FOREIGN KEY (persona_id) REFERENCES atlas.plataforma_persona (id) ON DELETE CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "soporte_aviso_teams",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "soporte_evento_calendario",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "soporte_feed_ics",
                schema: "atlas");
        }
    }
}
