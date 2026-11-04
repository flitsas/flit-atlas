using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Contratos.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class Exportaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contratos_exportacion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    desde_portal = table.Column<bool>(type: "boolean", nullable: false),
                    origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    paquete_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: true),
                    version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    formatos = table.Column<List<string>>(type: "text[]", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entrada_clave = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    entrada_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    objeto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    hash_contenido = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    archivos = table.Column<string>(type: "jsonb", nullable: false),
                    error_codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    solicitada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    iniciada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    terminada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    vence_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    vencida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos_exportacion", x => x.id);
                    table.CheckConstraint("ck_contratos_exportacion_archivos", "jsonb_typeof(archivos) = 'array'");
                    table.CheckConstraint("ck_contratos_exportacion_externa", "organizacion_id IS NULL OR (desde_portal AND origen = 'paquete')");
                    table.CheckConstraint("ck_contratos_exportacion_hash", "hash_contenido IS NULL OR hash_contenido ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_contratos_exportacion_lista", "estado <> 'lista' OR (objeto IS NOT NULL AND hash_contenido IS NOT NULL AND terminada_en IS NOT NULL AND vence_en IS NOT NULL)");
                    table.CheckConstraint("ck_contratos_exportacion_origen", "(origen = 'paquete' AND paquete_id IS NOT NULL AND revision IS NOT NULL AND version_id IS NULL) OR (origen = 'version' AND version_id IS NOT NULL AND paquete_id IS NULL AND revision IS NULL)");
                    table.ForeignKey(
                        name: "fk_contratos_exportacion_contratos_paquete_paquete_id",
                        column: x => x.paquete_id,
                        principalSchema: "atlas",
                        principalTable: "contratos_paquete",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contratos_exportacion_contratos_version_version_id",
                        column: x => x.version_id,
                        principalSchema: "atlas",
                        principalTable: "contratos_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Exportaciones de paquetes y versiones: quién, origen, formatos, estado y objetos en el S3 (HU 4.5, ATLAS-ADR-0042).");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_exportacion_estado_vence_en",
                schema: "atlas",
                table: "contratos_exportacion",
                columns: new[] { "estado", "vence_en" });

            migrationBuilder.CreateIndex(
                name: "ix_contratos_exportacion_organizacion_id",
                schema: "atlas",
                table: "contratos_exportacion",
                column: "organizacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_exportacion_paquete_id",
                schema: "atlas",
                table: "contratos_exportacion",
                column: "paquete_id");

            migrationBuilder.CreateIndex(
                name: "ix_contratos_exportacion_solicitada_por_solicitada_en",
                schema: "atlas",
                table: "contratos_exportacion",
                columns: new[] { "solicitada_por", "solicitada_en" });

            migrationBuilder.CreateIndex(
                name: "ix_contratos_exportacion_version_id",
                schema: "atlas",
                table: "contratos_exportacion",
                column: "version_id");

            // A mano (ATLAS-ADR-0023 y ATLAS-ADR-0026): la llave foránea hacia la organización del núcleo, que el contexto
            // del módulo no mapea, y RLS forzada con la función del núcleo, como toda tabla con organizacion_id. Una
            // exportación interna (sin organización) solo la ve un alcance total: nunca un externo.
            migrationBuilder.Sql("""
                ALTER TABLE atlas.contratos_exportacion ADD CONSTRAINT fk_contratos_exportacion_plataforma_organizacion_organizacion_id
                    FOREIGN KEY (organizacion_id) REFERENCES atlas.plataforma_organizacion (id) ON DELETE RESTRICT;
                ALTER TABLE atlas.contratos_exportacion ENABLE ROW LEVEL SECURITY;
                ALTER TABLE atlas.contratos_exportacion FORCE ROW LEVEL SECURITY;
                CREATE POLICY organizacion_visible ON atlas.contratos_exportacion
                    USING (atlas.plataforma_organizacion_visible(organizacion_id))
                    WITH CHECK (atlas.plataforma_organizacion_visible(organizacion_id));
                COMMENT ON COLUMN atlas.contratos_exportacion.solicitada_por IS 'Persona que la pidió (id de plataforma_persona, sin llave foránea: es del núcleo).';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS organizacion_visible ON atlas.contratos_exportacion;");

            migrationBuilder.DropTable(
                name: "contratos_exportacion",
                schema: "atlas");
        }
    }
}
