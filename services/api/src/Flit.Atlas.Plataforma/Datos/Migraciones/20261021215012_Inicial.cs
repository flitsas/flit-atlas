using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Plataforma.Datos.Migraciones
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
                name: "plataforma_auditoria",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_tipo = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    organizacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    recurso_tipo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    recurso_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ip = table.Column<IPAddress>(type: "inet", nullable: true, comment: "@pii"),
                    datos = table.Column<string>(type: "jsonb", nullable: true),
                    correlacion_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_auditoria", x => x.id);
                },
                comment: "Solo inserción: UPDATE, DELETE y TRUNCATE rechazados.");

            migrationBuilder.CreateTable(
                name: "plataforma_configuracion",
                schema: "atlas",
                columns: table => new
                {
                    clave = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    valor = table.Column<string>(type: "jsonb", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_configuracion", x => x.clave);
                });

            migrationBuilder.CreateTable(
                name: "plataforma_inbox",
                schema: "atlas",
                columns: table => new
                {
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumidor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    procesado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_inbox", x => new { x.evento_id, x.consumidor });
                });

            migrationBuilder.CreateTable(
                name: "plataforma_modulo",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    organizaciones_habilitadas = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_modulo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plataforma_organizacion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    estado = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    admin_organizacion_habilitado = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_organizacion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plataforma_outbox",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tipo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sobre = table.Column<string>(type: "text", nullable: false),
                    ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    publicado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    ultimo_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plataforma_rol",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_rol", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plataforma_persona",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    organizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "@pii"),
                    correo = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false, comment: "@pii"),
                    entra_oid = table.Column<Guid>(type: "uuid", nullable: true),
                    cargo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    area = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    telefono = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, comment: "@pii"),
                    estado = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_persona", x => x.id);
                    table.ForeignKey(
                        name: "fk_plataforma_persona_plataforma_organizacion_organizacion_id",
                        column: x => x.organizacion_id,
                        principalSchema: "atlas",
                        principalTable: "plataforma_organizacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plataforma_mapeo_grupo_entra",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    grupo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grupo_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_mapeo_grupo_entra", x => x.id);
                    table.ForeignKey(
                        name: "fk_plataforma_mapeo_grupo_entra_plataforma_rol_rol_id",
                        column: x => x.rol_id,
                        principalSchema: "atlas",
                        principalTable: "plataforma_rol",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plataforma_aceptacion_terminos",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    aceptado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip = table.Column<IPAddress>(type: "inet", nullable: true, comment: "@pii"),
                    agente_usuario = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_aceptacion_terminos", x => x.id);
                    table.ForeignKey(
                        name: "fk_plataforma_aceptacion_terminos_plataforma_persona_persona_id",
                        column: x => x.persona_id,
                        principalSchema: "atlas",
                        principalTable: "plataforma_persona",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plataforma_asignacion_rol",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    alcance_tipo = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    alcance_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    vigente_desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    vigente_hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    origen = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    asignado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_asignacion_rol", x => x.id);
                    table.ForeignKey(
                        name: "fk_plataforma_asignacion_rol_plataforma_organizacion_organizac",
                        column: x => x.organizacion_id,
                        principalSchema: "atlas",
                        principalTable: "plataforma_organizacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plataforma_asignacion_rol_plataforma_persona_asignado_por",
                        column: x => x.asignado_por,
                        principalSchema: "atlas",
                        principalTable: "plataforma_persona",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_plataforma_asignacion_rol_plataforma_persona_persona_id",
                        column: x => x.persona_id,
                        principalSchema: "atlas",
                        principalTable: "plataforma_persona",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_plataforma_asignacion_rol_plataforma_rol_rol_id",
                        column: x => x.rol_id,
                        principalSchema: "atlas",
                        principalTable: "plataforma_rol",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plataforma_invitacion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correo = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false, comment: "@pii"),
                    rol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alcance = table.Column<string>(type: "jsonb", nullable: false),
                    permisos_extra = table.Column<string>(type: "jsonb", nullable: false),
                    acceso_vence_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    token_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    token_vence_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    invitado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    aceptada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_invitacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_plataforma_invitacion_plataforma_organizacion_organizacion_",
                        column: x => x.organizacion_id,
                        principalSchema: "atlas",
                        principalTable: "plataforma_organizacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plataforma_invitacion_plataforma_persona_invitado_por",
                        column: x => x.invitado_por,
                        principalSchema: "atlas",
                        principalTable: "plataforma_persona",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plataforma_invitacion_plataforma_persona_persona_id",
                        column: x => x.persona_id,
                        principalSchema: "atlas",
                        principalTable: "plataforma_persona",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_plataforma_invitacion_plataforma_rol_rol_id",
                        column: x => x.rol_id,
                        principalSchema: "atlas",
                        principalTable: "plataforma_rol",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plataforma_notificacion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cuerpo = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    enlace = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    leida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_notificacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_plataforma_notificacion_plataforma_persona_persona_id",
                        column: x => x.persona_id,
                        principalSchema: "atlas",
                        principalTable: "plataforma_persona",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plataforma_preferencia_notificacion",
                schema: "atlas",
                columns: table => new
                {
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    canal = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_preferencia_notificacion", x => new { x.persona_id, x.tipo, x.canal });
                    table.ForeignKey(
                        name: "fk_plataforma_preferencia_notificacion_plataforma_persona_pers",
                        column: x => x.persona_id,
                        principalSchema: "atlas",
                        principalTable: "plataforma_persona",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plataforma_suscripcion_push",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false, comment: "@pii"),
                    p256dh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    auth = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dispositivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ultimo_uso_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_suscripcion_push", x => x.id);
                    table.ForeignKey(
                        name: "fk_plataforma_suscripcion_push_plataforma_persona_persona_id",
                        column: x => x.persona_id,
                        principalSchema: "atlas",
                        principalTable: "plataforma_persona",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_aceptacion_terminos_persona_id_documento_version",
                schema: "atlas",
                table: "plataforma_aceptacion_terminos",
                columns: new[] { "persona_id", "documento", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_asignacion_rol_asignado_por",
                schema: "atlas",
                table: "plataforma_asignacion_rol",
                column: "asignado_por");

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_asignacion_rol_organizacion_id",
                schema: "atlas",
                table: "plataforma_asignacion_rol",
                column: "organizacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_asignacion_rol_persona_id",
                schema: "atlas",
                table: "plataforma_asignacion_rol",
                column: "persona_id");

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_asignacion_rol_rol_id",
                schema: "atlas",
                table: "plataforma_asignacion_rol",
                column: "rol_id");

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_auditoria_ocurrido_en",
                schema: "atlas",
                table: "plataforma_auditoria",
                column: "ocurrido_en");

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_auditoria_organizacion_id_ocurrido_en",
                schema: "atlas",
                table: "plataforma_auditoria",
                columns: new[] { "organizacion_id", "ocurrido_en" });

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_auditoria_recurso_tipo_recurso_id",
                schema: "atlas",
                table: "plataforma_auditoria",
                columns: new[] { "recurso_tipo", "recurso_id" });

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_invitacion_invitado_por",
                schema: "atlas",
                table: "plataforma_invitacion",
                column: "invitado_por");

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_invitacion_organizacion_id_estado",
                schema: "atlas",
                table: "plataforma_invitacion",
                columns: new[] { "organizacion_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_invitacion_persona_id",
                schema: "atlas",
                table: "plataforma_invitacion",
                column: "persona_id");

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_invitacion_rol_id",
                schema: "atlas",
                table: "plataforma_invitacion",
                column: "rol_id");

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_invitacion_token_hash",
                schema: "atlas",
                table: "plataforma_invitacion",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_mapeo_grupo_entra_grupo_id_rol_id",
                schema: "atlas",
                table: "plataforma_mapeo_grupo_entra",
                columns: new[] { "grupo_id", "rol_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_mapeo_grupo_entra_rol_id",
                schema: "atlas",
                table: "plataforma_mapeo_grupo_entra",
                column: "rol_id");

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_notificacion_persona_id_leida_en",
                schema: "atlas",
                table: "plataforma_notificacion",
                columns: new[] { "persona_id", "leida_en" });

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_organizacion_nombre",
                schema: "atlas",
                table: "plataforma_organizacion",
                column: "nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_outbox_pendientes",
                schema: "atlas",
                table: "plataforma_outbox",
                columns: new[] { "ocurrido_en", "id" },
                filter: "publicado_en IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_persona_correo",
                schema: "atlas",
                table: "plataforma_persona",
                column: "correo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_persona_entra_oid",
                schema: "atlas",
                table: "plataforma_persona",
                column: "entra_oid",
                unique: true,
                filter: "entra_oid IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_persona_organizacion_id",
                schema: "atlas",
                table: "plataforma_persona",
                column: "organizacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_rol_codigo",
                schema: "atlas",
                table: "plataforma_rol",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_suscripcion_push_endpoint",
                schema: "atlas",
                table: "plataforma_suscripcion_push",
                column: "endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_suscripcion_push_persona_id",
                schema: "atlas",
                table: "plataforma_suscripcion_push",
                column: "persona_id");

            // Auditoría de solo inserción (HU 1.2, prompt maestro §8.9). Dos capas:
            // 1. Sin permisos de UPDATE, DELETE ni TRUNCATE, tampoco para el usuario que migra, que en dev/qa/pdn es
            //    el mismo de la aplicación (flit_atlas, dueño del esquema según el script de flit).
            // 2. Un trigger que rechaza esas operaciones aunque alguien devuelva los permisos o entre como superusuario.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION atlas.plataforma_auditoria_solo_insercion() RETURNS trigger
                LANGUAGE plpgsql AS $fn$
                BEGIN
                    RAISE EXCEPTION 'atlas.plataforma_auditoria es de solo inserción: % no está permitido', TG_OP
                        USING ERRCODE = '42501';
                END
                $fn$;

                CREATE TRIGGER tr_plataforma_auditoria_sin_cambios
                    BEFORE UPDATE OR DELETE ON atlas.plataforma_auditoria
                    FOR EACH ROW EXECUTE FUNCTION atlas.plataforma_auditoria_solo_insercion();

                CREATE TRIGGER tr_plataforma_auditoria_sin_truncate
                    BEFORE TRUNCATE ON atlas.plataforma_auditoria
                    FOR EACH STATEMENT EXECUTE FUNCTION atlas.plataforma_auditoria_solo_insercion();

                REVOKE UPDATE, DELETE, TRUNCATE ON atlas.plataforma_auditoria FROM PUBLIC;
                REVOKE UPDATE, DELETE, TRUNCATE ON atlas.plataforma_auditoria FROM CURRENT_USER;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plataforma_aceptacion_terminos",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "plataforma_asignacion_rol",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "plataforma_auditoria",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "plataforma_configuracion",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "plataforma_inbox",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "plataforma_invitacion",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "plataforma_mapeo_grupo_entra",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "plataforma_modulo",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "plataforma_notificacion",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "plataforma_outbox",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "plataforma_preferencia_notificacion",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "plataforma_suscripcion_push",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "plataforma_rol",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "plataforma_persona",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "plataforma_organizacion",
                schema: "atlas");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS atlas.plataforma_auditoria_solo_insercion();");
        }
    }
}
