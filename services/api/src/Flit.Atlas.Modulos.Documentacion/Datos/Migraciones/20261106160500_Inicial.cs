using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Flit.Atlas.Modulos.Documentacion.Datos.Migraciones
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
                name: "documentacion_documento",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    aplicativo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ruta = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    repositorio = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    visibilidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    audiencias = table.Column<List<string>>(type: "text[]", nullable: false),
                    responsable = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "@pii"),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version_actual = table.Column<int>(type: "integer", nullable: false),
                    version_vigente = table.Column<int>(type: "integer", nullable: true),
                    revision_actual = table.Column<int>(type: "integer", nullable: false),
                    revision_publicada = table.Column<int>(type: "integer", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentacion_documento", x => x.id);
                    table.CheckConstraint("ck_documentacion_documento_origen", "(origen = 'repo' AND ruta IS NOT NULL AND repositorio IS NOT NULL) OR (origen = 'atlas' AND ruta IS NULL AND repositorio IS NULL)");
                },
                comment: "Identidad estable de cada documento, con la foto de su versión vigente (ATLAS-ADR-0053).");

            migrationBuilder.CreateTable(
                name: "documentacion_revision",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    restaurada_de = table.Column<int>(type: "integer", nullable: true),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contenido = table.Column<string>(type: "text", nullable: false),
                    visibilidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    audiencias = table.Column<List<string>>(type: "text[]", nullable: false),
                    responsable = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "@pii"),
                    nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "@pii"),
                    autor_persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guardada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    huella = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentacion_revision", x => x.id);
                    table.CheckConstraint("ck_documentacion_revision_huella", "huella ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_documentacion_revision_documentacion_documento_documento_id",
                        column: x => x.documento_id,
                        principalSchema: "atlas",
                        principalTable: "documentacion_documento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Revisiones inmutables de lo escrito en Atlas: foto, autor, fecha, nota y huella (ATLAS-ADR-0053).");

            migrationBuilder.CreateTable(
                name: "documentacion_version",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contenido = table.Column<string>(type: "text", nullable: false),
                    visibilidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    audiencias = table.Column<List<string>>(type: "text[]", nullable: false),
                    responsable = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "@pii"),
                    es_borrador = table.Column<bool>(type: "boolean", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ruta = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    repositorio = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    rama = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    commit_sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    workflow = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    workflow_del_job = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ejecucion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    revision_origen = table.Column<int>(type: "integer", nullable: true),
                    publicado_por = table.Column<string>(type: "character varying(410)", maxLength: 410, nullable: true),
                    aprobada_por_persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    publicado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    busqueda = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('spanish'::regconfig, lower(translate(titulo, 'áàäâãÁÀÄÂÃéèëêÉÈËÊíìïîÍÌÏÎóòöôõÓÒÖÔÕúùüûÚÙÜÛñÑçÇ', 'aaaaaAAAAAeeeeEEEEiiiiIIIIoooooOOOOOuuuuUUUUnNcC'))), 'A') || setweight(to_tsvector('spanish'::regconfig, lower(translate(contenido, 'áàäâãÁÀÄÂÃéèëêÉÈËÊíìïîÍÌÏÎóòöôõÓÒÖÔÕúùüûÚÙÜÛñÑçÇ', 'aaaaaAAAAAeeeeEEEEiiiiIIIIoooooOOOOOuuuuUUUUnNcC'))), 'B')", stored: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentacion_version", x => x.id);
                    table.UniqueConstraint("ak_documentacion_version_documento_id_numero", x => new { x.documento_id, x.numero });
                    table.CheckConstraint("ck_documentacion_version_sha256", "sha256 ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_documentacion_version_documentacion_documento_documento_id",
                        column: x => x.documento_id,
                        principalSchema: "atlas",
                        principalTable: "documentacion_documento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Versiones inmutables de cada documento: foto, sha256 y procedencia (ATLAS-ADR-0053).");

            migrationBuilder.CreateTable(
                name: "documentacion_canal",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ambiente = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version_numero = table.Column<int>(type: "integer", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentacion_canal", x => x.id);
                    table.ForeignKey(
                        name: "fk_documentacion_canal_documentacion_documento_documento_id",
                        column: x => x.documento_id,
                        principalSchema: "atlas",
                        principalTable: "documentacion_documento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documentacion_canal_documentacion_version_documento_id_vers",
                        columns: x => new { x.documento_id, x.version_numero },
                        principalSchema: "atlas",
                        principalTable: "documentacion_version",
                        principalColumns: new[] { "documento_id", "numero" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Versión de cada documento por ambiente (ATLAS-ADR-0053).");

            migrationBuilder.CreateIndex(
                name: "ix_documentacion_canal_documento_id_ambiente",
                schema: "atlas",
                table: "documentacion_canal",
                columns: new[] { "documento_id", "ambiente" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documentacion_canal_documento_id_version_numero",
                schema: "atlas",
                table: "documentacion_canal",
                columns: new[] { "documento_id", "version_numero" });

            migrationBuilder.CreateIndex(
                name: "ix_documentacion_documento_aplicativo_id",
                schema: "atlas",
                table: "documentacion_documento",
                column: "aplicativo_id");

            migrationBuilder.CreateIndex(
                name: "ix_documentacion_documento_aplicativo_id_ruta",
                schema: "atlas",
                table: "documentacion_documento",
                columns: new[] { "aplicativo_id", "ruta" },
                unique: true,
                filter: "ruta IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_documentacion_documento_repositorio",
                schema: "atlas",
                table: "documentacion_documento",
                column: "repositorio");

            migrationBuilder.CreateIndex(
                name: "ix_documentacion_revision_documento_id_numero",
                schema: "atlas",
                table: "documentacion_revision",
                columns: new[] { "documento_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documentacion_version_busqueda",
                schema: "atlas",
                table: "documentacion_version",
                column: "busqueda")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_documentacion_version_documento_id_sha256",
                schema: "atlas",
                table: "documentacion_version",
                columns: new[] { "documento_id", "sha256" },
                unique: true);

            // Inmutabilidad en la base (ATLAS-ADR-0053 §2), además de la regla de la API: una versión o una revisión no cambia.
            // Borrar (purgar) sigue siendo posible, como decisión explícita.
            migrationBuilder.Sql("""
                CREATE FUNCTION atlas.documentacion_inmutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Las versiones y las revisiones de un documento son inmutables (ATLAS-ADR-0053).'
                        USING ERRCODE = 'check_violation';
                END $$;
                CREATE TRIGGER documentacion_version_inmutable BEFORE UPDATE ON atlas.documentacion_version
                    FOR EACH ROW EXECUTE FUNCTION atlas.documentacion_inmutable();
                CREATE TRIGGER documentacion_revision_inmutable BEFORE UPDATE ON atlas.documentacion_revision
                    FOR EACH ROW EXECUTE FUNCTION atlas.documentacion_inmutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER documentacion_version_inmutable ON atlas.documentacion_version;
                DROP TRIGGER documentacion_revision_inmutable ON atlas.documentacion_revision;
                DROP FUNCTION atlas.documentacion_inmutable();
                """);

            migrationBuilder.DropTable(
                name: "documentacion_canal",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "documentacion_revision",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "documentacion_version",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "documentacion_documento",
                schema: "atlas");
        }
    }
}
