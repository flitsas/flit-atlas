using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Catalogo.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class FuenteIctProto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La semilla se aplica una sola vez por aplicativo y repo: la fuente ya sembrada de core-ict no recibe sus proto de otra forma.
            migrationBuilder.Sql("""
                UPDATE atlas.catalogo_fuente_publicacion f
                SET tipos = array_append(f.tipos, 'proto')
                FROM atlas.catalogo_aplicativo a
                WHERE a.id = f.aplicativo_id AND a.slug = 'flit2-core-ict' AND f.repositorio = 'flitsas/flit' AND NOT 'proto' = ANY (f.tipos);
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE atlas.catalogo_fuente_publicacion f
                SET tipos = array_remove(f.tipos, 'proto')
                FROM atlas.catalogo_aplicativo a
                WHERE a.id = f.aplicativo_id AND a.slug = 'flit2-core-ict' AND f.repositorio = 'flitsas/flit' AND cardinality(f.tipos) > 1;
                """);

        }
    }
}
