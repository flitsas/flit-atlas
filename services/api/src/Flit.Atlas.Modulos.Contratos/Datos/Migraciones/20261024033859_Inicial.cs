using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Contratos.Datos.Migraciones
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
                name: "contratos_contrato",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    aplicativo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    visibilidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    repositorio = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos_contrato", x => x.id);
                    table.CheckConstraint("ck_contratos_contrato_codigo", "codigo ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                },
                comment: "Contratos de integración de cada aplicativo (plan 00 §8.4, ATLAS-ADR-0035).");

            migrationBuilder.CreateTable(
                name: "contratos_version",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    mayor = table.Column<int>(type: "integer", nullable: false),
                    menor = table.Column<int>(type: "integer", nullable: false),
                    parche = table.Column<int>(type: "integer", nullable: false),
                    prerelease = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    es_estable = table.Column<bool>(type: "boolean", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    formato = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    contenido = table.Column<string>(type: "text", nullable: false),
                    tamano = table.Column<int>(type: "integer", nullable: false),
                    repositorio = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rama = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    commit_sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    workflow = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    workflow_del_job = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ejecucion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    publicado_por = table.Column<string>(type: "character varying(310)", maxLength: 310, nullable: false),
                    publicado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changelog = table.Column<string>(type: "jsonb", nullable: false),
                    tiene_breaking = table.Column<bool>(type: "boolean", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    retiro_en = table.Column<DateOnly>(type: "date", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos_version", x => x.id);
                    table.CheckConstraint("ck_contratos_version_estable", "es_estable = (prerelease IS NULL)");
                    table.CheckConstraint("ck_contratos_version_sha256", "sha256 ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_contratos_version_contratos_contrato_contrato_id",
                        column: x => x.contrato_id,
                        principalSchema: "atlas",
                        principalTable: "contratos_contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Versiones inmutables de cada contrato: semver, sha256, spec y procedencia (ATLAS-ADR-0035).");

            migrationBuilder.CreateTable(
                name: "contratos_operacion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    clase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    metodo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ruta = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    operation_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    resumen = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    etiquetas = table.Column<List<string>>(type: "text[]", nullable: false),
                    publica = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos_operacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_contratos_operacion_contratos_version_version_id",
                        column: x => x.version_id,
                        principalSchema: "atlas",
                        principalTable: "contratos_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Índice de operaciones, webhooks, canales y rpc de cada versión (plan 00 §8.4).");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_contrato_aplicativo_id_codigo",
                schema: "atlas",
                table: "contratos_contrato",
                columns: new[] { "aplicativo_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contratos_operacion_operation_id",
                schema: "atlas",
                table: "contratos_operacion",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_operacion_version_id_clave",
                schema: "atlas",
                table: "contratos_operacion",
                columns: new[] { "version_id", "clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contratos_version_contrato_id_publicado_en",
                schema: "atlas",
                table: "contratos_version",
                columns: new[] { "contrato_id", "publicado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_contratos_version_contrato_id_version",
                schema: "atlas",
                table: "contratos_version",
                columns: new[] { "contrato_id", "version" },
                unique: true);

            // Inmutabilidad en la base (ATLAS-ADR-0035), además de la regla de la API: de una versión publicada solo cambian
            // su estado, su fecha de retiro y su marca de actualización; su índice de operaciones no cambia nunca. Borrar
            // (purgar) sigue siendo posible, como decisión explícita.
            migrationBuilder.Sql("""
                CREATE FUNCTION atlas.contratos_version_inmutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF (NEW.id, NEW.contrato_id, NEW.version, NEW.mayor, NEW.menor, NEW.parche, NEW.prerelease, NEW.es_estable,
                        NEW.sha256, NEW.formato, NEW.contenido, NEW.tamano, NEW.repositorio, NEW.rama, NEW.commit_sha, NEW.workflow,
                        NEW.workflow_del_job, NEW.ejecucion, NEW.publicado_por, NEW.publicado_en, NEW.changelog, NEW.tiene_breaking,
                        NEW.creado_en)
                       IS DISTINCT FROM
                       (OLD.id, OLD.contrato_id, OLD.version, OLD.mayor, OLD.menor, OLD.parche, OLD.prerelease, OLD.es_estable,
                        OLD.sha256, OLD.formato, OLD.contenido, OLD.tamano, OLD.repositorio, OLD.rama, OLD.commit_sha, OLD.workflow,
                        OLD.workflow_del_job, OLD.ejecucion, OLD.publicado_por, OLD.publicado_en, OLD.changelog, OLD.tiene_breaking,
                        OLD.creado_en) THEN
                        RAISE EXCEPTION 'Una versión publicada de un contrato es inmutable: publica una versión nueva (ATLAS-ADR-0035).'
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER contratos_version_inmutable BEFORE UPDATE ON atlas.contratos_version
                    FOR EACH ROW EXECUTE FUNCTION atlas.contratos_version_inmutable();

                CREATE FUNCTION atlas.contratos_operacion_inmutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'El índice de operaciones de una versión publicada es inmutable (ATLAS-ADR-0035).'
                        USING ERRCODE = 'check_violation';
                END $$;
                CREATE TRIGGER contratos_operacion_inmutable BEFORE UPDATE ON atlas.contratos_operacion
                    FOR EACH ROW EXECUTE FUNCTION atlas.contratos_operacion_inmutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS contratos_operacion_inmutable ON atlas.contratos_operacion;
                DROP FUNCTION IF EXISTS atlas.contratos_operacion_inmutable();
                DROP TRIGGER IF EXISTS contratos_version_inmutable ON atlas.contratos_version;
                DROP FUNCTION IF EXISTS atlas.contratos_version_inmutable();
                """);

            migrationBuilder.DropTable(
                name: "contratos_operacion",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "contratos_version",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "contratos_contrato",
                schema: "atlas");
        }
    }
}
