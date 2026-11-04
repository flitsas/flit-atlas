using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Identidad.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class SesionesYSeguridad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<IPAddress>(
                name: "ip",
                schema: "atlas",
                table: "identidad_sesion",
                type: "inet",
                nullable: true,
                comment: "@pii");

            // Las sesiones abiertas antes de esta migración quedan con sello vacío: no coinciden con el del usuario y se
            // pide entrar de nuevo una vez (ATLAS-ADR-0022).
            migrationBuilder.AddColumn<string>(
                name: "sello_seguridad",
                schema: "atlas",
                table: "identidad_sesion",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "identidad_evento_seguridad",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tipo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sesion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ip = table.Column<IPAddress>(type: "inet", nullable: true, comment: "@pii"),
                    agente_resumen = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    datos = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identidad_evento_seguridad", x => x.id);
                },
                comment: "Solo inserción: UPDATE, DELETE y TRUNCATE rechazados.");

            migrationBuilder.CreateIndex(
                name: "ix_identidad_evento_seguridad_ocurrido_en",
                schema: "atlas",
                table: "identidad_evento_seguridad",
                column: "ocurrido_en");

            migrationBuilder.CreateIndex(
                name: "ix_identidad_evento_seguridad_usuario_id_tipo_ocurrido_en",
                schema: "atlas",
                table: "identidad_evento_seguridad",
                columns: new[] { "usuario_id", "tipo", "ocurrido_en" });

            // Igual que plataforma_auditoria: solo inserción, también para el dueño de la tabla.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION atlas.identidad_evento_seguridad_solo_insercion() RETURNS trigger
                LANGUAGE plpgsql AS $fn$
                BEGIN
                    RAISE EXCEPTION 'atlas.identidad_evento_seguridad es de solo inserción: % no está permitido', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $fn$;

                CREATE TRIGGER tr_identidad_evento_seguridad_sin_cambios
                    BEFORE UPDATE OR DELETE ON atlas.identidad_evento_seguridad
                    FOR EACH ROW EXECUTE FUNCTION atlas.identidad_evento_seguridad_solo_insercion();

                CREATE TRIGGER tr_identidad_evento_seguridad_sin_truncate
                    BEFORE TRUNCATE ON atlas.identidad_evento_seguridad
                    FOR EACH STATEMENT EXECUTE FUNCTION atlas.identidad_evento_seguridad_solo_insercion();

                REVOKE UPDATE, DELETE, TRUNCATE ON atlas.identidad_evento_seguridad FROM PUBLIC;
                REVOKE UPDATE, DELETE, TRUNCATE ON atlas.identidad_evento_seguridad FROM CURRENT_USER;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identidad_evento_seguridad",
                schema: "atlas");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS atlas.identidad_evento_seguridad_solo_insercion();");

            migrationBuilder.DropColumn(
                name: "ip",
                schema: "atlas",
                table: "identidad_sesion");

            migrationBuilder.DropColumn(
                name: "sello_seguridad",
                schema: "atlas",
                table: "identidad_sesion");
        }
    }
}
