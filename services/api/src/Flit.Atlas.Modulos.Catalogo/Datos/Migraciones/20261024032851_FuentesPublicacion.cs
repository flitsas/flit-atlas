using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Catalogo.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class FuentesPublicacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalogo_fuente_publicacion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    aplicativo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repositorio = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ramas = table.Column<List<string>>(type: "text[]", nullable: false),
                    workflow = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tipos = table.Column<List<string>>(type: "text[]", nullable: false),
                    nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogo_fuente_publicacion", x => x.id);
                    table.CheckConstraint("ck_catalogo_fuente_publicacion_ramas", "cardinality(ramas) > 0");
                    table.CheckConstraint("ck_catalogo_fuente_publicacion_repositorio", "repositorio ~ '^[a-z0-9][a-z0-9_.-]*/[a-z0-9][a-z0-9_.-]*$'");
                    table.CheckConstraint("ck_catalogo_fuente_publicacion_tipos", "cardinality(tipos) > 0 AND tipos <@ ARRAY['openapi', 'asyncapi', 'proto', 'likec4', 'docs']::text[]");
                    table.ForeignKey(
                        name: "fk_catalogo_fuente_publicacion_catalogo_aplicativo_aplicativo_",
                        column: x => x.aplicativo_id,
                        principalSchema: "atlas",
                        principalTable: "catalogo_aplicativo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Lista blanca de publicación desde la CI con el OIDC de GitHub Actions (ATLAS-ADR-0035).");

            migrationBuilder.CreateIndex(
                name: "ix_catalogo_fuente_publicacion_aplicativo_id_repositorio_workf",
                schema: "atlas",
                table: "catalogo_fuente_publicacion",
                columns: new[] { "aplicativo_id", "repositorio", "workflow" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_catalogo_fuente_publicacion_repositorio",
                schema: "atlas",
                table: "catalogo_fuente_publicacion",
                column: "repositorio");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalogo_fuente_publicacion",
                schema: "atlas");
        }
    }
}
