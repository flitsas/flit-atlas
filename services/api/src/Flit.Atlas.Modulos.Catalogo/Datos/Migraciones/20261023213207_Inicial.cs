using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Catalogo.Datos.Migraciones
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
                name: "catalogo_aplicativo",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    padre_id = table.Column<Guid>(type: "uuid", nullable: true),
                    grupo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    criticidad = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    maneja_datos_personales = table.Column<bool>(type: "boolean", nullable: false),
                    repo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ruta_en_repo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tecnologias = table.Column<List<string>>(type: "text[]", nullable: false),
                    ambientes = table.Column<List<string>>(type: "text[]", nullable: false),
                    etiquetas = table.Column<List<string>>(type: "text[]", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogo_aplicativo", x => x.id);
                    table.CheckConstraint("ck_catalogo_aplicativo_producto_sin_padre", "(tipo = 'producto') = (padre_id IS NULL)");
                    table.CheckConstraint("ck_catalogo_aplicativo_slug", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                    table.ForeignKey(
                        name: "fk_catalogo_aplicativo_catalogo_aplicativo_padre_id",
                        column: x => x.padre_id,
                        principalSchema: "atlas",
                        principalTable: "catalogo_aplicativo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Catálogo de aplicativos: producto → módulo → servicio (ATLAS-ADR-0028).");

            migrationBuilder.CreateTable(
                name: "catalogo_semilla",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogo_semilla", x => x.id);
                },
                comment: "Entradas de la semilla del catálogo ya aplicadas (no se vuelven a crear).");

            migrationBuilder.CreateIndex(
                name: "ix_catalogo_aplicativo_grupo_estado",
                schema: "atlas",
                table: "catalogo_aplicativo",
                columns: new[] { "grupo", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_catalogo_aplicativo_padre_id",
                schema: "atlas",
                table: "catalogo_aplicativo",
                column: "padre_id");

            migrationBuilder.CreateIndex(
                name: "ix_catalogo_aplicativo_slug",
                schema: "atlas",
                table: "catalogo_aplicativo",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalogo_semilla_slug",
                schema: "atlas",
                table: "catalogo_semilla",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalogo_aplicativo",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "catalogo_semilla",
                schema: "atlas");
        }
    }
}
