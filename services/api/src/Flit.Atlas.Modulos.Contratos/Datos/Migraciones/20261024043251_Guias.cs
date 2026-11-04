using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Contratos.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class Guias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contratos_guia",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    operacion = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    aplica_desde = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    aplica_hasta = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    visibilidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revision_actual = table.Column<int>(type: "integer", nullable: false),
                    revision_publicada = table.Column<int>(type: "integer", nullable: true),
                    publicada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    publicada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    aprobada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos_guia", x => x.id);
                    table.CheckConstraint("ck_contratos_guia_revisiones", "revision_actual >= 1 AND (revision_publicada IS NULL OR revision_publicada BETWEEN 1 AND revision_actual)");
                    table.ForeignKey(
                        name: "fk_contratos_guia_contratos_contrato_contrato_id",
                        column: x => x.contrato_id,
                        principalSchema: "atlas",
                        principalTable: "contratos_contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Guías de integración de cada contrato, con la foto de su última revisión (HU 3.7, ATLAS-ADR-0040).");

            migrationBuilder.CreateTable(
                name: "contratos_guia_revision",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    restaurada_de = table.Column<int>(type: "integer", nullable: true),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contenido = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false),
                    operacion = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    aplica_desde = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    aplica_hasta = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    visibilidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "@pii texto libre del autor"),
                    autor_persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guardada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    huella = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos_guia_revision", x => x.id);
                    table.CheckConstraint("ck_contratos_guia_revision_huella", "huella ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_contratos_guia_revision_restaurada", "(origen = 'restauracion') = (restaurada_de IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_contratos_guia_revision_contratos_guia_guia_id",
                        column: x => x.guia_id,
                        principalSchema: "atlas",
                        principalTable: "contratos_guia",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Revisiones inmutables de cada guía: foto completa, autor, fecha, nota y huella (HU 3.7, ATLAS-ADR-0040).");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_guia_contrato_id",
                schema: "atlas",
                table: "contratos_guia",
                column: "contrato_id");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_guia_revision_guia_id_numero",
                schema: "atlas",
                table: "contratos_guia_revision",
                columns: new[] { "guia_id", "numero" },
                unique: true);

            // Inmutabilidad en la base (ATLAS-ADR-0040), además de la API: una revisión guardada no cambia nunca;
            // restaurar crea otra. Borrar (purgar) sigue siendo posible como decisión explícita, sin endpoint.
            migrationBuilder.Sql("""
                CREATE FUNCTION atlas.contratos_guia_revision_inmutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Una revisión de una guía es inmutable: guarda o restaura para crear otra (ATLAS-ADR-0040).'
                        USING ERRCODE = 'check_violation';
                END $$;
                CREATE TRIGGER contratos_guia_revision_inmutable BEFORE UPDATE ON atlas.contratos_guia_revision
                    FOR EACH ROW EXECUTE FUNCTION atlas.contratos_guia_revision_inmutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS contratos_guia_revision_inmutable ON atlas.contratos_guia_revision;
                DROP FUNCTION IF EXISTS atlas.contratos_guia_revision_inmutable();
                """);

            migrationBuilder.DropTable(
                name: "contratos_guia_revision",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "contratos_guia",
                schema: "atlas");
        }
    }
}
