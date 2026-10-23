using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Plataforma.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class SuscripcionesPushYBandeja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "fallos_consecutivos",
                schema: "atlas",
                table: "plataforma_suscripcion_push",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ultimo_fallo_en",
                schema: "atlas",
                table: "plataforma_suscripcion_push",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_notificacion_persona_id_creado_en",
                schema: "atlas",
                table: "plataforma_notificacion",
                columns: new[] { "persona_id", "creado_en" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_plataforma_notificacion_persona_id_creado_en",
                schema: "atlas",
                table: "plataforma_notificacion");

            migrationBuilder.DropColumn(
                name: "fallos_consecutivos",
                schema: "atlas",
                table: "plataforma_suscripcion_push");

            migrationBuilder.DropColumn(
                name: "ultimo_fallo_en",
                schema: "atlas",
                table: "plataforma_suscripcion_push");
        }
    }
}
