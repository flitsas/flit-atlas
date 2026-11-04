using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Equipo.Datos.Migraciones
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
                name: "equipo_responsabilidad",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    aplicativo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dedicacion = table.Column<int>(type: "integer", nullable: true),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    vigente_hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    estado_traspaso = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: false),
                    orden_aplicativo = table.Column<int>(type: "integer", nullable: false),
                    orden_persona = table.Column<int>(type: "integer", nullable: false),
                    orden_en_persona = table.Column<int>(type: "integer", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_equipo_responsabilidad", x => x.id);
                    table.CheckConstraint("ck_equipo_responsabilidad_dedicacion", "dedicacion IS NULL OR (dedicacion BETWEEN 1 AND 100)");
                    table.CheckConstraint("ck_equipo_responsabilidad_vigencia", "vigente_hasta IS NULL OR vigente_hasta >= vigente_desde");
                },
                comment: "Responsables principal y secundario de cada aplicativo del catálogo (reparto vigente).");

            migrationBuilder.CreateTable(
                name: "equipo_responsabilidad_version",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    origen = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    restaurada_de = table.Column<int>(type: "integer", nullable: true),
                    fecha_documento = table.Column<DateOnly>(type: "date", nullable: true),
                    definido_por = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "@pii"),
                    autor_persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "@pii"),
                    definiciones = table.Column<string>(type: "jsonb", nullable: false),
                    contenido = table.Column<string>(type: "jsonb", nullable: false, comment: "@pii"),
                    huella = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_equipo_responsabilidad_version", x => x.id);
                    table.CheckConstraint("ck_equipo_responsabilidad_version_numero", "numero >= 1");
                },
                comment: "Historial del reparto de responsables: cada versión es una foto completa, con autor y nota.");

            migrationBuilder.CreateTable(
                name: "equipo_revision_programada",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    estado = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    recordatorio_enviado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hecha_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hecha_por_persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "@pii"),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_equipo_revision_programada", x => x.id);
                },
                comment: "Revisiones trimestrales del reparto y su recordatorio.");

            migrationBuilder.CreateTable(
                name: "equipo_usuario_github",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: false, comment: "@pii"),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_equipo_usuario_github", x => x.id);
                },
                comment: "Usuario de GitHub de cada persona, para el bloque CODEOWNERS.");

            migrationBuilder.CreateTable(
                name: "equipo_traspaso_checklist",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    responsabilidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "@pii"),
                    registrada_por_persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_equipo_traspaso_checklist", x => x.id);
                    table.ForeignKey(
                        name: "fk_equipo_traspaso_checklist_equipo_responsabilidad_responsabi",
                        column: x => x.responsabilidad_id,
                        principalSchema: "atlas",
                        principalTable: "equipo_responsabilidad",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Checklist del traspaso acompañado: despliegues y guardias del secundario con el principal.");

            migrationBuilder.CreateIndex(
                name: "ix_equipo_responsabilidad_aplicativo_id_tipo",
                schema: "atlas",
                table: "equipo_responsabilidad",
                columns: new[] { "aplicativo_id", "tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_equipo_responsabilidad_persona_id",
                schema: "atlas",
                table: "equipo_responsabilidad",
                column: "persona_id");

            migrationBuilder.CreateIndex(
                name: "ix_equipo_responsabilidad_version_numero",
                schema: "atlas",
                table: "equipo_responsabilidad_version",
                column: "numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_equipo_revision_pendiente",
                schema: "atlas",
                table: "equipo_revision_programada",
                column: "estado",
                unique: true,
                filter: "estado = 'pendiente'");

            migrationBuilder.CreateIndex(
                name: "ix_equipo_traspaso_checklist_responsabilidad_id_persona_id",
                schema: "atlas",
                table: "equipo_traspaso_checklist",
                columns: new[] { "responsabilidad_id", "persona_id" });

            migrationBuilder.CreateIndex(
                name: "ix_equipo_usuario_github_persona_id",
                schema: "atlas",
                table: "equipo_usuario_github",
                column: "persona_id",
                unique: true);

            // Llaves hacia el núcleo (se migra antes): las personas no se borran mientras tengan responsabilidades o
            // versiones; el usuario de GitHub se va con la persona. Al catálogo no hay llave: un módulo no depende de las
            // tablas de otro (ATLAS-ADR-0031).
            migrationBuilder.Sql("""
                ALTER TABLE atlas.equipo_responsabilidad ADD CONSTRAINT fk_equipo_responsabilidad_plataforma_persona_persona_id
                    FOREIGN KEY (persona_id) REFERENCES atlas.plataforma_persona (id) ON DELETE RESTRICT;
                ALTER TABLE atlas.equipo_traspaso_checklist ADD CONSTRAINT fk_equipo_traspaso_checklist_plataforma_persona_persona_id
                    FOREIGN KEY (persona_id) REFERENCES atlas.plataforma_persona (id) ON DELETE RESTRICT;
                ALTER TABLE atlas.equipo_responsabilidad_version ADD CONSTRAINT fk_equipo_responsabilidad_version_plataforma_persona_autor
                    FOREIGN KEY (autor_persona_id) REFERENCES atlas.plataforma_persona (id) ON DELETE RESTRICT;
                ALTER TABLE atlas.equipo_usuario_github ADD CONSTRAINT fk_equipo_usuario_github_plataforma_persona_persona_id
                    FOREIGN KEY (persona_id) REFERENCES atlas.plataforma_persona (id) ON DELETE CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "equipo_responsabilidad_version",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "equipo_revision_programada",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "equipo_traspaso_checklist",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "equipo_usuario_github",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "equipo_responsabilidad",
                schema: "atlas");
        }
    }
}
