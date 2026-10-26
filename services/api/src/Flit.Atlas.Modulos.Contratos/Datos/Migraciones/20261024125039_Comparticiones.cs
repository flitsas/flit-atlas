using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Contratos.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class Comparticiones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contratos_comparticion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    paquete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_fijada = table.Column<int>(type: "integer", nullable: true),
                    puede_exportar = table.Column<bool>(type: "boolean", nullable: false),
                    puede_sandbox = table.Column<bool>(type: "boolean", nullable: false),
                    vigente_desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    vigente_hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    otorgado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    revocado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos_comparticion", x => x.id);
                    table.CheckConstraint("ck_contratos_comparticion_revision", "revision_fijada IS NULL OR revision_fijada >= 1");
                    table.CheckConstraint("ck_contratos_comparticion_revocada", "(revocado_en IS NULL) = (revocado_por IS NULL)");
                    table.CheckConstraint("ck_contratos_comparticion_vigencia", "vigente_hasta IS NULL OR vigente_hasta > vigente_desde");
                    table.ForeignKey(
                        name: "fk_contratos_comparticion_contratos_paquete_paquete_id",
                        column: x => x.paquete_id,
                        principalSchema: "atlas",
                        principalTable: "contratos_paquete",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Paquetes compartidos con organizaciones: revisión fijada, exportar, sandbox y vigencia (HU 4.3, ATLAS-ADR-0043).");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_comparticion_organizacion_id",
                schema: "atlas",
                table: "contratos_comparticion",
                column: "organizacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_comparticion_paquete_id_organizacion_id",
                schema: "atlas",
                table: "contratos_comparticion",
                columns: new[] { "paquete_id", "organizacion_id" },
                unique: true);

            // A mano (ATLAS-ADR-0023, ATLAS-ADR-0026 y ATLAS-ADR-0043), como toda tabla con organizacion_id: la llave
            // foránea hacia el núcleo, que el contexto del módulo no mapea; la revisión fijada debe existir en el paquete
            // (la unicidad (paquete_id, numero) de las revisiones es su llave); y RLS forzada con la función del núcleo
            // (el núcleo se migra antes que los módulos, así que la función ya existe).
            migrationBuilder.Sql("""
                ALTER TABLE atlas.contratos_comparticion ADD CONSTRAINT fk_contratos_comparticion_plataforma_organizacion_organizacion_id
                    FOREIGN KEY (organizacion_id) REFERENCES atlas.plataforma_organizacion (id) ON DELETE RESTRICT;
                ALTER TABLE atlas.contratos_comparticion ADD CONSTRAINT fk_contratos_comparticion_revision_fijada
                    FOREIGN KEY (paquete_id, revision_fijada) REFERENCES atlas.contratos_paquete_revision (paquete_id, numero) ON DELETE RESTRICT;
                ALTER TABLE atlas.contratos_comparticion ENABLE ROW LEVEL SECURITY;
                ALTER TABLE atlas.contratos_comparticion FORCE ROW LEVEL SECURITY;
                CREATE POLICY organizacion_visible ON atlas.contratos_comparticion
                    USING (atlas.plataforma_organizacion_visible(organizacion_id))
                    WITH CHECK (atlas.plataforma_organizacion_visible(organizacion_id));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS organizacion_visible ON atlas.contratos_comparticion;");

            migrationBuilder.DropTable(
                name: "contratos_comparticion",
                schema: "atlas");
        }
    }
}
