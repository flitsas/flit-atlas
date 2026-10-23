using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Identidad.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class ServidorTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identidad_oidc_aplicacion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    client_secret = table.Column<string>(type: "text", nullable: true),
                    client_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    consent_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    json_web_key_set = table.Column<string>(type: "text", nullable: true),
                    permissions = table.Column<string>(type: "text", nullable: true),
                    post_logout_redirect_uris = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redirect_uris = table.Column<string>(type: "text", nullable: true),
                    requirements = table.Column<string>(type: "text", nullable: true),
                    settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identidad_oidc_aplicacion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "identidad_oidc_scope",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    descriptions = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identidad_oidc_scope", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "identidad_oidc_autorizacion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    scopes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identidad_oidc_autorizacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_identidad_oidc_autorizacion_identidad_oidc_aplicacion_appli",
                        column: x => x.application_id,
                        principalSchema: "atlas",
                        principalTable: "identidad_oidc_aplicacion",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "identidad_oidc_token",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    authorization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expiration_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    payload = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redemption_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reference_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identidad_oidc_token", x => x.id);
                    table.ForeignKey(
                        name: "fk_identidad_oidc_token_identidad_oidc_aplicacion_application_",
                        column: x => x.application_id,
                        principalSchema: "atlas",
                        principalTable: "identidad_oidc_aplicacion",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_identidad_oidc_token_identidad_oidc_autorizacion_authorizat",
                        column: x => x.authorization_id,
                        principalSchema: "atlas",
                        principalTable: "identidad_oidc_autorizacion",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_identidad_oidc_aplicacion_client_id",
                schema: "atlas",
                table: "identidad_oidc_aplicacion",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identidad_oidc_autorizacion_application_id_status_subject_t",
                schema: "atlas",
                table: "identidad_oidc_autorizacion",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_identidad_oidc_scope_name",
                schema: "atlas",
                table: "identidad_oidc_scope",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identidad_oidc_token_application_id_status_subject_type",
                schema: "atlas",
                table: "identidad_oidc_token",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_identidad_oidc_token_authorization_id",
                schema: "atlas",
                table: "identidad_oidc_token",
                column: "authorization_id");

            migrationBuilder.CreateIndex(
                name: "ix_identidad_oidc_token_reference_id",
                schema: "atlas",
                table: "identidad_oidc_token",
                column: "reference_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identidad_oidc_scope",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "identidad_oidc_token",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "identidad_oidc_autorizacion",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "identidad_oidc_aplicacion",
                schema: "atlas");
        }
    }
}
