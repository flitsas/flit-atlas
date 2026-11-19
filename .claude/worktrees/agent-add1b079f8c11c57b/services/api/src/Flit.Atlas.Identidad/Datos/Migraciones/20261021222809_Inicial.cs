using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Flit.Atlas.Identidad.Datos.Migraciones
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
                name: "identidad_rol_identity",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identidad_rol_identity", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "identidad_usuario",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "@pii"),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "@pii"),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "@pii"),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "@pii"),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "@pii"),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identidad_usuario", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "identidad_rol_claim",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identidad_rol_claim", x => x.id);
                    table.ForeignKey(
                        name: "fk_identidad_rol_claim_identidad_rol_identity_role_id",
                        column: x => x.role_id,
                        principalSchema: "atlas",
                        principalTable: "identidad_rol_identity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identidad_sesion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultimo_uso_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_absoluta_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ticket = table.Column<byte[]>(type: "bytea", nullable: false),
                    agente_resumen = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    revocada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_revocacion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identidad_sesion", x => x.id);
                    table.ForeignKey(
                        name: "fk_identidad_sesion_identidad_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalSchema: "atlas",
                        principalTable: "identidad_usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identidad_usuario_claim",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identidad_usuario_claim", x => x.id);
                    table.ForeignKey(
                        name: "fk_identidad_usuario_claim_identidad_usuario_user_id",
                        column: x => x.user_id,
                        principalSchema: "atlas",
                        principalTable: "identidad_usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identidad_usuario_login",
                schema: "atlas",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identidad_usuario_login", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_identidad_usuario_login_identidad_usuario_user_id",
                        column: x => x.user_id,
                        principalSchema: "atlas",
                        principalTable: "identidad_usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identidad_usuario_passkey",
                schema: "atlas",
                columns: table => new
                {
                    credential_id = table.Column<byte[]>(type: "bytea", maxLength: 1024, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identidad_usuario_passkey", x => x.credential_id);
                    table.ForeignKey(
                        name: "fk_identidad_usuario_passkey_identidad_usuario_user_id",
                        column: x => x.user_id,
                        principalSchema: "atlas",
                        principalTable: "identidad_usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identidad_usuario_rol_identity",
                schema: "atlas",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identidad_usuario_rol_identity", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_identidad_usuario_rol_identity_identidad_rol_identity_role_",
                        column: x => x.role_id,
                        principalSchema: "atlas",
                        principalTable: "identidad_rol_identity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_identidad_usuario_rol_identity_identidad_usuario_user_id",
                        column: x => x.user_id,
                        principalSchema: "atlas",
                        principalTable: "identidad_usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identidad_usuario_token",
                schema: "atlas",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identidad_usuario_token", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_identidad_usuario_token_identidad_usuario_user_id",
                        column: x => x.user_id,
                        principalSchema: "atlas",
                        principalTable: "identidad_usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_identidad_rol_claim_role_id",
                schema: "atlas",
                table: "identidad_rol_claim",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "atlas",
                table: "identidad_rol_identity",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identidad_sesion_usuario_id_revocada_en",
                schema: "atlas",
                table: "identidad_sesion",
                columns: new[] { "usuario_id", "revocada_en" });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "atlas",
                table: "identidad_usuario",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ix_identidad_usuario_persona_id",
                schema: "atlas",
                table: "identidad_usuario",
                column: "persona_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "atlas",
                table: "identidad_usuario",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identidad_usuario_claim_user_id",
                schema: "atlas",
                table: "identidad_usuario_claim",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_identidad_usuario_login_user_id",
                schema: "atlas",
                table: "identidad_usuario_login",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_identidad_usuario_passkey_user_id",
                schema: "atlas",
                table: "identidad_usuario_passkey",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_identidad_usuario_rol_identity_role_id",
                schema: "atlas",
                table: "identidad_usuario_rol_identity",
                column: "role_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identidad_rol_claim",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "identidad_sesion",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "identidad_usuario_claim",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "identidad_usuario_login",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "identidad_usuario_passkey",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "identidad_usuario_rol_identity",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "identidad_usuario_token",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "identidad_rol_identity",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "identidad_usuario",
                schema: "atlas");
        }
    }
}
