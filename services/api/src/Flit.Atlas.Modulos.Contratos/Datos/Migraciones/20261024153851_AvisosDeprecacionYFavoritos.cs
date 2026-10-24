using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Contratos.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class AvisosDeprecacionYFavoritos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contratos_aviso_deprecacion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    retiro_en = table.Column<DateOnly>(type: "date", nullable: false),
                    momento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dias = table.Column<int>(type: "integer", nullable: true),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    enviado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos_aviso_deprecacion", x => x.id);
                    table.CheckConstraint("ck_contratos_aviso_deprecacion_dias", "(momento = 'recordatorio') = (dias IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_contratos_aviso_deprecacion_contratos_version_version_id",
                        column: x => x.version_id,
                        principalSchema: "atlas",
                        principalTable: "contratos_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Avisos de deprecación enviados por persona y canal: la marca que los hace idempotentes (HU 4.8, ATLAS-ADR-0046).");

            migrationBuilder.CreateTable(
                name: "contratos_favorito",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    paquete_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guia_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos_favorito", x => x.id);
                    table.CheckConstraint("ck_contratos_favorito_tipo", "(tipo = 'contrato' AND contrato_id IS NOT NULL AND paquete_id IS NULL AND guia_id IS NULL) OR (tipo = 'paquete' AND paquete_id IS NOT NULL AND contrato_id IS NULL AND guia_id IS NULL) OR (tipo = 'contrato_compartido' AND paquete_id IS NOT NULL AND contrato_id IS NOT NULL AND guia_id IS NULL) OR (tipo = 'guia' AND paquete_id IS NOT NULL AND guia_id IS NOT NULL AND contrato_id IS NULL)");
                },
                comment: "Contratos, paquetes y guías favoritos de cada persona, para verlos sin conexión (HU 4.9, ATLAS-ADR-0034).");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_aviso_deprecacion_clave",
                schema: "atlas",
                table: "contratos_aviso_deprecacion",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contratos_aviso_deprecacion_organizacion_id",
                schema: "atlas",
                table: "contratos_aviso_deprecacion",
                column: "organizacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_aviso_deprecacion_version_id_retiro_en",
                schema: "atlas",
                table: "contratos_aviso_deprecacion",
                columns: new[] { "version_id", "retiro_en" });

            migrationBuilder.CreateIndex(
                name: "ix_contratos_favorito_organizacion_id",
                schema: "atlas",
                table: "contratos_favorito",
                column: "organizacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_favorito_persona_id_tipo_paquete_id_contrato_id_g",
                schema: "atlas",
                table: "contratos_favorito",
                columns: new[] { "persona_id", "tipo", "paquete_id", "contrato_id", "guia_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            // A mano (ATLAS-ADR-0023, ATLAS-ADR-0026 y ATLAS-ADR-0046), como toda tabla con organizacion_id: las llaves
            // foráneas hacia el núcleo, que el contexto del módulo no mapea (la persona se lleva sus avisos y favoritos si se
            // suprime, Ley 1581), y RLS forzada con la función del núcleo.
            migrationBuilder.Sql("""
                ALTER TABLE atlas.contratos_aviso_deprecacion ADD CONSTRAINT fk_contratos_aviso_deprecacion_plataforma_organizacion_organizacion_id
                    FOREIGN KEY (organizacion_id) REFERENCES atlas.plataforma_organizacion (id) ON DELETE RESTRICT;
                ALTER TABLE atlas.contratos_aviso_deprecacion ADD CONSTRAINT fk_contratos_aviso_deprecacion_plataforma_persona_persona_id
                    FOREIGN KEY (persona_id) REFERENCES atlas.plataforma_persona (id) ON DELETE CASCADE;
                ALTER TABLE atlas.contratos_aviso_deprecacion ENABLE ROW LEVEL SECURITY;
                ALTER TABLE atlas.contratos_aviso_deprecacion FORCE ROW LEVEL SECURITY;
                CREATE POLICY organizacion_visible ON atlas.contratos_aviso_deprecacion
                    USING (atlas.plataforma_organizacion_visible(organizacion_id))
                    WITH CHECK (atlas.plataforma_organizacion_visible(organizacion_id));

                ALTER TABLE atlas.contratos_favorito ADD CONSTRAINT fk_contratos_favorito_plataforma_organizacion_organizacion_id
                    FOREIGN KEY (organizacion_id) REFERENCES atlas.plataforma_organizacion (id) ON DELETE RESTRICT;
                ALTER TABLE atlas.contratos_favorito ADD CONSTRAINT fk_contratos_favorito_plataforma_persona_persona_id
                    FOREIGN KEY (persona_id) REFERENCES atlas.plataforma_persona (id) ON DELETE CASCADE;
                ALTER TABLE atlas.contratos_favorito ENABLE ROW LEVEL SECURITY;
                ALTER TABLE atlas.contratos_favorito FORCE ROW LEVEL SECURITY;
                CREATE POLICY organizacion_visible ON atlas.contratos_favorito
                    USING (atlas.plataforma_organizacion_visible(organizacion_id))
                    WITH CHECK (atlas.plataforma_organizacion_visible(organizacion_id));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS organizacion_visible ON atlas.contratos_favorito;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS organizacion_visible ON atlas.contratos_aviso_deprecacion;");

            migrationBuilder.DropTable(
                name: "contratos_aviso_deprecacion",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "contratos_favorito",
                schema: "atlas");
        }
    }
}
