using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Contratos.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class Sandbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contratos_sandbox",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paquete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparticion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nivel = table.Column<int>(type: "integer", nullable: false),
                    ruta = table.Column<string>(type: "character(32)", fixedLength: true, maxLength: 32, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    contratos = table.Column<string>(type: "jsonb", nullable: false),
                    huella = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    llave_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    llave_creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    activado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    importado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    apagado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_apagado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    por_borrar = table.Column<string>(type: "jsonb", nullable: false),
                    ultimo_uso_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos_sandbox", x => x.id);
                    table.CheckConstraint("ck_contratos_sandbox_apagado", "(estado = 'apagado') = (apagado_en IS NOT NULL)");
                    table.CheckConstraint("ck_contratos_sandbox_contratos", "jsonb_typeof(contratos) = 'array' AND jsonb_typeof(por_borrar) = 'array'");
                    table.CheckConstraint("ck_contratos_sandbox_huella", "huella ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_contratos_sandbox_llave", "octet_length(llave_hash) = 32");
                    table.CheckConstraint("ck_contratos_sandbox_nivel", "nivel = 1");
                    table.CheckConstraint("ck_contratos_sandbox_ruta", "ruta ~ '^[0-9a-f]{32}$'");
                    table.ForeignKey(
                        name: "fk_contratos_sandbox_contratos_comparticion_comparticion_id",
                        column: x => x.comparticion_id,
                        principalSchema: "atlas",
                        principalTable: "contratos_comparticion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contratos_sandbox_contratos_paquete_paquete_id",
                        column: x => x.paquete_id,
                        principalSchema: "atlas",
                        principalTable: "contratos_paquete",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Sandbox nivel 1 (Microcks) de un paquete por organización: spec podado importado y hash de la llave del proxy (HU 4.7, ATLAS-ADR-0045).");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_sandbox_comparticion_id",
                schema: "atlas",
                table: "contratos_sandbox",
                column: "comparticion_id");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_sandbox_estado",
                schema: "atlas",
                table: "contratos_sandbox",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_sandbox_organizacion_id",
                schema: "atlas",
                table: "contratos_sandbox",
                column: "organizacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_sandbox_paquete_id_organizacion_id",
                schema: "atlas",
                table: "contratos_sandbox",
                columns: new[] { "paquete_id", "organizacion_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contratos_sandbox_ruta",
                schema: "atlas",
                table: "contratos_sandbox",
                column: "ruta",
                unique: true);

            // A mano (ATLAS-ADR-0023, ATLAS-ADR-0043 y ATLAS-ADR-0045), como toda tabla con organizacion_id: la llave
            // foránea hacia el núcleo, que el contexto del módulo no mapea, y RLS forzada con la función del núcleo. Un
            // externo de A, aunque el código olvidara filtrar, no ve ni escribe sandboxes de B.
            migrationBuilder.Sql("""
                ALTER TABLE atlas.contratos_sandbox ADD CONSTRAINT fk_contratos_sandbox_plataforma_organizacion_organizacion_id
                    FOREIGN KEY (organizacion_id) REFERENCES atlas.plataforma_organizacion (id) ON DELETE RESTRICT;
                ALTER TABLE atlas.contratos_sandbox ENABLE ROW LEVEL SECURITY;
                ALTER TABLE atlas.contratos_sandbox FORCE ROW LEVEL SECURITY;
                CREATE POLICY organizacion_visible ON atlas.contratos_sandbox
                    USING (atlas.plataforma_organizacion_visible(organizacion_id))
                    WITH CHECK (atlas.plataforma_organizacion_visible(organizacion_id));
                COMMENT ON COLUMN atlas.contratos_sandbox.llave_hash IS 'SHA-256 de la llave del sandbox; la llave en claro solo se muestra una vez y nunca se guarda.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS organizacion_visible ON atlas.contratos_sandbox;");

            migrationBuilder.DropTable(
                name: "contratos_sandbox",
                schema: "atlas");
        }
    }
}
