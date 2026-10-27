using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Contratos.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class Paquetes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contratos_paquete",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revision_actual = table.Column<int>(type: "integer", nullable: false),
                    revision_publicada = table.Column<int>(type: "integer", nullable: true),
                    publicada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    publicada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    archivado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    archivado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos_paquete", x => x.id);
                    table.CheckConstraint("ck_contratos_paquete_estado", "(estado <> 'publicado' OR revision_publicada IS NOT NULL) AND (estado <> 'borrador' OR revision_publicada IS NULL) AND ((estado = 'archivado') = (archivado_en IS NOT NULL))");
                    table.CheckConstraint("ck_contratos_paquete_revisiones", "revision_actual >= 1 AND (revision_publicada IS NULL OR revision_publicada BETWEEN 1 AND revision_actual)");
                },
                comment: "Paquetes de contratos para clientes, con la foto de su última revisión (HU 4.1, ATLAS-ADR-0041).");

            migrationBuilder.CreateTable(
                name: "contratos_paquete_revision",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    paquete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    definicion = table.Column<string>(type: "jsonb", nullable: false),
                    nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "@pii texto libre del autor"),
                    autor_persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guardada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    huella = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos_paquete_revision", x => x.id);
                    table.CheckConstraint("ck_contratos_paquete_revision_definicion", "jsonb_typeof(definicion) = 'array'");
                    table.CheckConstraint("ck_contratos_paquete_revision_huella", "huella ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_contratos_paquete_revision_contratos_paquete_paquete_id",
                        column: x => x.paquete_id,
                        principalSchema: "atlas",
                        principalTable: "contratos_paquete",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Revisiones inmutables de cada paquete: nombre, descripción, definición, autor, fecha, nota y huella (HU 4.1, ATLAS-ADR-0041).");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_paquete_estado",
                schema: "atlas",
                table: "contratos_paquete",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_paquete_revision_paquete_id_numero",
                schema: "atlas",
                table: "contratos_paquete_revision",
                columns: new[] { "paquete_id", "numero" },
                unique: true);

            // Inmutabilidad en la base (ATLAS-ADR-0041), además de la API: una revisión guardada no cambia nunca; fijar una
            // anterior la vuelve vigente sin tocarla. Borrar (purgar) sigue siendo una decisión explícita, sin endpoint.
            migrationBuilder.Sql("""
                CREATE FUNCTION atlas.contratos_paquete_revision_inmutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Una revisión de un paquete es inmutable: guarda otra o fija una anterior (ATLAS-ADR-0041).'
                        USING ERRCODE = 'check_violation';
                END $$;
                CREATE TRIGGER contratos_paquete_revision_inmutable BEFORE UPDATE ON atlas.contratos_paquete_revision
                    FOR EACH ROW EXECUTE FUNCTION atlas.contratos_paquete_revision_inmutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS contratos_paquete_revision_inmutable ON atlas.contratos_paquete_revision;
                DROP FUNCTION IF EXISTS atlas.contratos_paquete_revision_inmutable();
                """);

            migrationBuilder.DropTable(
                name: "contratos_paquete_revision",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "contratos_paquete",
                schema: "atlas");
        }
    }
}
