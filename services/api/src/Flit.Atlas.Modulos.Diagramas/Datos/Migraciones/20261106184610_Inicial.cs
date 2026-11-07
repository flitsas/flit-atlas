using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Diagramas.Datos.Migraciones
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
                name: "diagramas_diagrama",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    aplicativo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    visibilidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version_actual = table.Column<int>(type: "integer", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diagramas_diagrama", x => x.id);
                },
                comment: "Identidad estable de cada diagrama, con la foto de su última versión (ATLAS-ADR-0054).");

            migrationBuilder.CreateTable(
                name: "diagramas_comentario",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    diagrama_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_numero = table.Column<int>(type: "integer", nullable: false),
                    nodo_clave = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    texto = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false, comment: "@pii"),
                    escrito_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diagramas_comentario", x => x.id);
                    table.ForeignKey(
                        name: "fk_diagramas_comentario_diagramas_diagrama_diagrama_id",
                        column: x => x.diagrama_id,
                        principalSchema: "atlas",
                        principalTable: "diagramas_diagrama",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Comentarios por versión y, si se quiere, por nodo (ATLAS-ADR-0054).");

            migrationBuilder.CreateTable(
                name: "diagramas_version",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    diagrama_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    restaurada_de = table.Column<int>(type: "integer", nullable: true),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    visibilidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fuente = table.Column<string>(type: "text", nullable: false),
                    nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "@pii"),
                    autor_persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guardada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    huella = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diagramas_version", x => x.id);
                    table.CheckConstraint("ck_diagramas_version_huella", "huella ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_diagramas_version_diagramas_diagrama_diagrama_id",
                        column: x => x.diagrama_id,
                        principalSchema: "atlas",
                        principalTable: "diagramas_diagrama",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Versiones inmutables de cada diagrama: fuente, foto, autor, nota y huella (ATLAS-ADR-0054).");

            migrationBuilder.CreateTable(
                name: "diagramas_nodo",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    etiqueta = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    aplicativo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    elemento_likec4 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diagramas_nodo", x => x.id);
                    table.ForeignKey(
                        name: "fk_diagramas_nodo_diagramas_version_version_id",
                        column: x => x.version_id,
                        principalSchema: "atlas",
                        principalTable: "diagramas_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Nodos del lienzo por versión: proyección del JSON (ATLAS-ADR-0054).");

            migrationBuilder.CreateTable(
                name: "diagramas_relacion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    origen = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    destino = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    etiqueta = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diagramas_relacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_diagramas_relacion_diagramas_version_version_id",
                        column: x => x.version_id,
                        principalSchema: "atlas",
                        principalTable: "diagramas_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Relaciones del lienzo por versión: proyección del JSON (ATLAS-ADR-0054).");

            migrationBuilder.CreateIndex(
                name: "ix_diagramas_comentario_diagrama_id_escrito_en",
                schema: "atlas",
                table: "diagramas_comentario",
                columns: new[] { "diagrama_id", "escrito_en" });

            migrationBuilder.CreateIndex(
                name: "ix_diagramas_diagrama_aplicativo_id",
                schema: "atlas",
                table: "diagramas_diagrama",
                column: "aplicativo_id");

            migrationBuilder.CreateIndex(
                name: "ix_diagramas_diagrama_codigo",
                schema: "atlas",
                table: "diagramas_diagrama",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_diagramas_nodo_aplicativo_id",
                schema: "atlas",
                table: "diagramas_nodo",
                column: "aplicativo_id");

            migrationBuilder.CreateIndex(
                name: "ix_diagramas_nodo_version_id_clave",
                schema: "atlas",
                table: "diagramas_nodo",
                columns: new[] { "version_id", "clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_diagramas_relacion_version_id_clave",
                schema: "atlas",
                table: "diagramas_relacion",
                columns: new[] { "version_id", "clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_diagramas_version_diagrama_id_numero",
                schema: "atlas",
                table: "diagramas_version",
                columns: new[] { "diagrama_id", "numero" },
                unique: true);

            // Una versión no cambia ni se borra (ATLAS-ADR-0054 §2).
            migrationBuilder.Sql("""
                CREATE FUNCTION atlas.diagramas_inmutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Las versiones de un diagrama son inmutables (ATLAS-ADR-0054).'
                        USING ERRCODE = 'check_violation';
                END $$;
                CREATE TRIGGER diagramas_version_inmutable BEFORE UPDATE OR DELETE ON atlas.diagramas_version
                    FOR EACH ROW EXECUTE FUNCTION atlas.diagramas_inmutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER diagramas_version_inmutable ON atlas.diagramas_version;
                DROP FUNCTION atlas.diagramas_inmutable();
                """);

            migrationBuilder.DropTable(
                name: "diagramas_comentario",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "diagramas_nodo",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "diagramas_relacion",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "diagramas_version",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "diagramas_diagrama",
                schema: "atlas");
        }
    }
}
