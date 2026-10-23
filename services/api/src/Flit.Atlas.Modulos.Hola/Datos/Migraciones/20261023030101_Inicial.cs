using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Hola.Datos.Migraciones
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
                name: "hola_saludo",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    texto = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: false, comment: "@pii"),
                    autor_persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hola_saludo", x => x.id);
                },
                comment: "Módulo de ejemplo «hola» (docs/como-crear-un-modulo.md).");

            migrationBuilder.CreateIndex(
                name: "ix_hola_saludo_organizacion_id_creado_en",
                schema: "atlas",
                table: "hola_saludo",
                columns: new[] { "organizacion_id", "creado_en" });

            // A mano (ATLAS-ADR-0023 y ATLAS-ADR-0026): la llave foránea hacia el núcleo, que el contexto del módulo no
            // mapea, y RLS forzada con la función del núcleo, como toda tabla con organizacion_id. El núcleo se migra
            // antes que los módulos, así que la función ya existe.
            migrationBuilder.Sql("""
                ALTER TABLE atlas.hola_saludo ADD CONSTRAINT fk_hola_saludo_plataforma_organizacion_organizacion_id
                    FOREIGN KEY (organizacion_id) REFERENCES atlas.plataforma_organizacion (id) ON DELETE RESTRICT;
                ALTER TABLE atlas.hola_saludo ENABLE ROW LEVEL SECURITY;
                ALTER TABLE atlas.hola_saludo FORCE ROW LEVEL SECURITY;
                CREATE POLICY organizacion_visible ON atlas.hola_saludo
                    USING (atlas.plataforma_organizacion_visible(organizacion_id))
                    WITH CHECK (atlas.plataforma_organizacion_visible(organizacion_id));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS organizacion_visible ON atlas.hola_saludo;");

            migrationBuilder.DropTable(
                name: "hola_saludo",
                schema: "atlas");
        }
    }
}
