using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Contratos.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class CanalesPorAmbiente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "changelog_base",
                schema: "atlas",
                table: "contratos_version",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "contratos_canal",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ambiente = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    desplegado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    desplegado_por = table.Column<string>(type: "character varying(310)", maxLength: 310, nullable: false),
                    commit_sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ejecucion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos_canal", x => x.id);
                    table.CheckConstraint("ck_contratos_canal_ambiente", "ambiente ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                    table.ForeignKey(
                        name: "fk_contratos_canal_contratos_contrato_contrato_id",
                        column: x => x.contrato_id,
                        principalSchema: "atlas",
                        principalTable: "contratos_contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contratos_canal_contratos_version_version_id",
                        column: x => x.version_id,
                        principalSchema: "atlas",
                        principalTable: "contratos_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Versión de cada contrato que corre en cada ambiente (plan 00 §8.2, regla 5; HU 3.4).");

            migrationBuilder.CreateTable(
                name: "contratos_canal_historial",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ambiente = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_anterior_id = table.Column<Guid>(type: "uuid", nullable: true),
                    desplegado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    desplegado_por = table.Column<string>(type: "character varying(310)", maxLength: 310, nullable: false),
                    repositorio = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rama = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    commit_sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    workflow_del_job = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ejecucion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos_canal_historial", x => x.id);
                    table.ForeignKey(
                        name: "fk_contratos_canal_historial_contratos_contrato_contrato_id",
                        column: x => x.contrato_id,
                        principalSchema: "atlas",
                        principalTable: "contratos_contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contratos_canal_historial_contratos_version_version_anterio",
                        column: x => x.version_anterior_id,
                        principalSchema: "atlas",
                        principalTable: "contratos_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contratos_canal_historial_contratos_version_version_id",
                        column: x => x.version_id,
                        principalSchema: "atlas",
                        principalTable: "contratos_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Historial de los canales por ambiente, solo inserción (HU 3.4).");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_canal_contrato_id_ambiente",
                schema: "atlas",
                table: "contratos_canal",
                columns: new[] { "contrato_id", "ambiente" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contratos_canal_version_id",
                schema: "atlas",
                table: "contratos_canal",
                column: "version_id");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_canal_historial_contrato_id_ambiente_desplegado_en",
                schema: "atlas",
                table: "contratos_canal_historial",
                columns: new[] { "contrato_id", "ambiente", "desplegado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_contratos_canal_historial_version_anterior_id",
                schema: "atlas",
                table: "contratos_canal_historial",
                column: "version_anterior_id");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_canal_historial_version_id",
                schema: "atlas",
                table: "contratos_canal_historial",
                column: "version_id");

            // changelog_base también es inmutable (ATLAS-ADR-0035): se reemplaza la función del trigger con la columna nueva.
            // El historial de los canales es solo inserción (HU 3.4).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION atlas.contratos_version_inmutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF (NEW.id, NEW.contrato_id, NEW.version, NEW.mayor, NEW.menor, NEW.parche, NEW.prerelease, NEW.es_estable,
                        NEW.sha256, NEW.formato, NEW.contenido, NEW.tamano, NEW.repositorio, NEW.rama, NEW.commit_sha, NEW.workflow,
                        NEW.workflow_del_job, NEW.ejecucion, NEW.publicado_por, NEW.publicado_en, NEW.changelog, NEW.tiene_breaking,
                        NEW.changelog_base, NEW.creado_en)
                       IS DISTINCT FROM
                       (OLD.id, OLD.contrato_id, OLD.version, OLD.mayor, OLD.menor, OLD.parche, OLD.prerelease, OLD.es_estable,
                        OLD.sha256, OLD.formato, OLD.contenido, OLD.tamano, OLD.repositorio, OLD.rama, OLD.commit_sha, OLD.workflow,
                        OLD.workflow_del_job, OLD.ejecucion, OLD.publicado_por, OLD.publicado_en, OLD.changelog, OLD.tiene_breaking,
                        OLD.changelog_base, OLD.creado_en) THEN
                        RAISE EXCEPTION 'Una versión publicada de un contrato es inmutable: publica una versión nueva (ATLAS-ADR-0035).'
                            USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE FUNCTION atlas.contratos_canal_historial_inmutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'El historial de los canales es solo inserción (ATLAS-ADR-0035).'
                        USING ERRCODE = 'check_violation';
                END $$;
                CREATE TRIGGER contratos_canal_historial_inmutable BEFORE UPDATE ON atlas.contratos_canal_historial
                    FOR EACH ROW EXECUTE FUNCTION atlas.contratos_canal_historial_inmutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS contratos_canal_historial_inmutable ON atlas.contratos_canal_historial;
                DROP FUNCTION IF EXISTS atlas.contratos_canal_historial_inmutable();
                CREATE OR REPLACE FUNCTION atlas.contratos_version_inmutable() RETURNS trigger LANGUAGE plpgsql AS $$
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
                """);

            migrationBuilder.DropTable(
                name: "contratos_canal",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "contratos_canal_historial",
                schema: "atlas");

            migrationBuilder.DropColumn(
                name: "changelog_base",
                schema: "atlas",
                table: "contratos_version");
        }
    }
}
