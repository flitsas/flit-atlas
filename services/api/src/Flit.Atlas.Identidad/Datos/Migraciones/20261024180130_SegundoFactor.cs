using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Identidad.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class SegundoFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identidad_segundo_factor",
                schema: "atlas",
                columns: table => new
                {
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fallos = table.Column<int>(type: "integer", nullable: false),
                    ultimo_paso_totp = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identidad_segundo_factor", x => x.usuario_id);
                    table.ForeignKey(
                        name: "fk_identidad_segundo_factor_identidad_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalSchema: "atlas",
                        principalTable: "identidad_usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identidad_segundo_factor",
                schema: "atlas");
        }
    }
}
