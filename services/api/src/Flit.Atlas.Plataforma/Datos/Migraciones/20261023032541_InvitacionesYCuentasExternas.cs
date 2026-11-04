using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Plataforma.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class InvitacionesYCuentasExternas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "enviada_en",
                schema: "atlas",
                table: "plataforma_invitacion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "envios",
                schema: "atlas",
                table: "plataforma_invitacion",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "estado_cambiado_en",
                schema: "atlas",
                table: "plataforma_invitacion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "estado_cambiado_por",
                schema: "atlas",
                table: "plataforma_invitacion",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nombre",
                schema: "atlas",
                table: "plataforma_invitacion",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "@pii");

            migrationBuilder.AddColumn<string>(
                name: "hash_contenido",
                schema: "atlas",
                table: "plataforma_aceptacion_terminos",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "invitacion_id",
                schema: "atlas",
                table: "plataforma_aceptacion_terminos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_invitacion_correo",
                schema: "atlas",
                table: "plataforma_invitacion",
                column: "correo");

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_invitacion_estado_cambiado_por",
                schema: "atlas",
                table: "plataforma_invitacion",
                column: "estado_cambiado_por");

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_aceptacion_terminos_invitacion_id",
                schema: "atlas",
                table: "plataforma_aceptacion_terminos",
                column: "invitacion_id");

            migrationBuilder.AddForeignKey(
                name: "fk_plataforma_aceptacion_terminos_plataforma_invitacion_invita",
                schema: "atlas",
                table: "plataforma_aceptacion_terminos",
                column: "invitacion_id",
                principalSchema: "atlas",
                principalTable: "plataforma_invitacion",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_plataforma_invitacion_plataforma_persona_estado_cambiado_por",
                schema: "atlas",
                table: "plataforma_invitacion",
                column: "estado_cambiado_por",
                principalSchema: "atlas",
                principalTable: "plataforma_persona",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_plataforma_aceptacion_terminos_plataforma_invitacion_invita",
                schema: "atlas",
                table: "plataforma_aceptacion_terminos");

            migrationBuilder.DropForeignKey(
                name: "fk_plataforma_invitacion_plataforma_persona_estado_cambiado_por",
                schema: "atlas",
                table: "plataforma_invitacion");

            migrationBuilder.DropIndex(
                name: "ix_plataforma_invitacion_correo",
                schema: "atlas",
                table: "plataforma_invitacion");

            migrationBuilder.DropIndex(
                name: "ix_plataforma_invitacion_estado_cambiado_por",
                schema: "atlas",
                table: "plataforma_invitacion");

            migrationBuilder.DropIndex(
                name: "ix_plataforma_aceptacion_terminos_invitacion_id",
                schema: "atlas",
                table: "plataforma_aceptacion_terminos");

            migrationBuilder.DropColumn(
                name: "enviada_en",
                schema: "atlas",
                table: "plataforma_invitacion");

            migrationBuilder.DropColumn(
                name: "envios",
                schema: "atlas",
                table: "plataforma_invitacion");

            migrationBuilder.DropColumn(
                name: "estado_cambiado_en",
                schema: "atlas",
                table: "plataforma_invitacion");

            migrationBuilder.DropColumn(
                name: "estado_cambiado_por",
                schema: "atlas",
                table: "plataforma_invitacion");

            migrationBuilder.DropColumn(
                name: "nombre",
                schema: "atlas",
                table: "plataforma_invitacion");

            migrationBuilder.DropColumn(
                name: "hash_contenido",
                schema: "atlas",
                table: "plataforma_aceptacion_terminos");

            migrationBuilder.DropColumn(
                name: "invitacion_id",
                schema: "atlas",
                table: "plataforma_aceptacion_terminos");
        }
    }
}
