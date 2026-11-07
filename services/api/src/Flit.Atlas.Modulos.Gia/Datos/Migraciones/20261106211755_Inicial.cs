using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Gia.Datos.Migraciones
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
                name: "gia_caso_uso",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    aplicacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    aplicativo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    repo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    dueno_persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ambientes = table.Column<List<string>>(type: "text[]", nullable: false),
                    proposito = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    usuarios_afectados = table.Column<List<string>>(type: "text[]", nullable: false),
                    decision_sobre_personas = table.Column<bool>(type: "boolean", nullable: true),
                    decision_descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    humano_en_bucle = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    categorias_dato = table.Column<List<string>>(type: "text[]", nullable: false),
                    tipos_pii = table.Column<List<string>>(type: "text[]", nullable: false),
                    volumen_mensual_estimado = table.Column<int>(type: "integer", nullable: true),
                    base_legal = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    base_legal_ref = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    minimizacion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    presidio_activo = table.Column<bool>(type: "boolean", nullable: true),
                    presidio_entidades = table.Column<List<string>>(type: "text[]", nullable: false),
                    alias_usados = table.Column<List<string>>(type: "text[]", nullable: false),
                    proveedor_actual = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    transferencia_internacional = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    metricas_calidad = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    evals_ruta = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    prompts_ubicacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    controles = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    aviso_usuario = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    plan_rollback = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    enlaces = table.Column<List<string>>(type: "text[]", nullable: false),
                    riesgo_preliminar = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    detalle_plan = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    nivel_riesgo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    aprobado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    aprobado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    proxima_revision = table.Column<DateOnly>(type: "date", nullable: true),
                    version_actual = table.Column<int>(type: "integer", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gia_caso_uso", x => x.id);
                    table.CheckConstraint("ck_gia_caso_uso_estado", "estado IN ('borrador', 'en_revision', 'aprobado', 'en_produccion', 'suspendido', 'retirado')");
                    table.CheckConstraint("ck_gia_caso_uso_humano", "humano_en_bucle IS NULL OR humano_en_bucle IN ('ninguno', 'revision_posterior', 'aprobacion_previa')");
                    table.CheckConstraint("ck_gia_caso_uso_nivel", "nivel_riesgo IS NULL OR nivel_riesgo IN ('bajo', 'medio', 'alto', 'critico')");
                    table.CheckConstraint("ck_gia_caso_uso_preliminar", "riesgo_preliminar IS NULL OR riesgo_preliminar IN ('bajo', 'medio', 'medio_alto', 'alto')");
                    table.CheckConstraint("ck_gia_caso_uso_tipo", "tipo IS NULL OR tipo IN ('ocr', 'chat', 'clasificacion', 'generacion', 'agente', 'embeddings', 'otro')");
                },
                comment: "Casos de uso de IA: la ficha del plan 02 §7.1 (ATLAS-ADR-0055).");

            migrationBuilder.CreateTable(
                name: "gia_herramienta",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    plan = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    proveedor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    nivel_dato_maximo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    configuracion_requerida = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    costo_mensual_usd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    notas = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gia_herramienta", x => x.id);
                    table.CheckConstraint("ck_gia_herramienta_estado", "estado IN ('aprobada', 'en_evaluacion', 'prohibida')");
                    table.CheckConstraint("ck_gia_herramienta_nivel", "nivel_dato_maximo IN ('publico', 'interno', 'confidencial', 'personal', 'sensible')");
                },
                comment: "Herramientas de IA del equipo (plan 02 §12).");

            migrationBuilder.CreateTable(
                name: "gia_politica",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contenido = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false),
                    fuente_git = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    requiere_aceptacion = table.Column<bool>(type: "boolean", nullable: false),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: true),
                    aprobada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gia_politica", x => x.id);
                    table.CheckConstraint("ck_gia_politica_estado", "estado IN ('borrador', 'vigente', 'reemplazada')");
                },
                comment: "Versiones de las políticas de IA (plan 02 §6). Solo el borrador se edita.");

            migrationBuilder.CreateTable(
                name: "gia_rol_gobierno",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    asignado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    asignado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gia_rol_gobierno", x => x.id);
                    table.CheckConstraint("ck_gia_rol_gobierno_rol", "rol IN ('responsable_ia', 'opd', 'comite', 'seguridad')");
                },
                comment: "Quién tiene cada rol de gobierno: Responsable de IA, OPD, Comité o Seguridad (plan 02 §4.1).");

            migrationBuilder.CreateTable(
                name: "gia_caso_uso_version",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    caso_uso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    foto = table.Column<string>(type: "jsonb", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "@pii"),
                    autor_persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guardada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gia_caso_uso_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_gia_caso_uso_version_gia_caso_uso_caso_uso_id",
                        column: x => x.caso_uso_id,
                        principalSchema: "atlas",
                        principalTable: "gia_caso_uso",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Historial inmutable de la ficha de cada caso (ATLAS-ADR-0055).");

            migrationBuilder.CreateTable(
                name: "gia_eipd",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    caso_uso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    necesidad = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    riesgos = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    medidas = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    opinion_opd = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    plan_accion = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    documento_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    decidida_por = table.Column<Guid>(type: "uuid", nullable: true),
                    decidida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gia_eipd", x => x.id);
                    table.CheckConstraint("ck_gia_eipd_estado", "estado IN ('borrador', 'en_revision', 'aprobada', 'rechazada')");
                    table.ForeignKey(
                        name: "fk_gia_eipd_gia_caso_uso_caso_uso_id",
                        column: x => x.caso_uso_id,
                        principalSchema: "atlas",
                        principalTable: "gia_caso_uso",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Evaluación de impacto en privacidad de cada caso (plan 02 §8.4).");

            migrationBuilder.CreateTable(
                name: "gia_evaluacion_riesgo",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    caso_uso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    dimensiones = table.Column<string>(type: "jsonb", nullable: false),
                    puntaje_inherente = table.Column<int>(type: "integer", nullable: false),
                    nivel_inherente = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    puntaje_residual = table.Column<int>(type: "integer", nullable: false),
                    nivel_residual = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    principios = table.Column<string>(type: "jsonb", nullable: false),
                    transferencia_internacional = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    condiciones = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    evaluador_persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decidida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gia_evaluacion_riesgo", x => x.id);
                    table.CheckConstraint("ck_gia_evaluacion_decision", "decision IN ('pendiente', 'aprobado', 'aprobado_condiciones', 'rechazado')");
                    table.CheckConstraint("ck_gia_evaluacion_inherente", "nivel_inherente IN ('bajo', 'medio', 'alto', 'critico')");
                    table.CheckConstraint("ck_gia_evaluacion_residual", "nivel_residual IN ('bajo', 'medio', 'alto', 'critico')");
                    table.ForeignKey(
                        name: "fk_gia_evaluacion_riesgo_gia_caso_uso_caso_uso_id",
                        column: x => x.caso_uso_id,
                        principalSchema: "atlas",
                        principalTable: "gia_caso_uso",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Evaluaciones de riesgo por caso: seis dimensiones, nivel por la peor (plan 02 §8).");

            migrationBuilder.CreateTable(
                name: "gia_incidente",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    severidad = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    caso_uso_id = table.Column<Guid>(type: "uuid", nullable: true),
                    descripcion = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false, comment: "@pii"),
                    detectado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    contenido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cerrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acciones = table.Column<List<string>>(type: "text[]", nullable: false),
                    reportado_sic = table.Column<bool>(type: "boolean", nullable: false),
                    responsable_persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lecciones = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gia_incidente", x => x.id);
                    table.CheckConstraint("ck_gia_incidente_severidad", "severidad IN ('S1', 'S2', 'S3', 'S4')");
                    table.CheckConstraint("ck_gia_incidente_tipo", "tipo IN ('INC-IA-01', 'INC-IA-02', 'INC-IA-03', 'INC-IA-04', 'INC-IA-05', 'INC-IA-06', 'INC-IA-07')");
                    table.ForeignKey(
                        name: "fk_gia_incidente_gia_caso_uso_caso_uso_id",
                        column: x => x.caso_uso_id,
                        principalSchema: "atlas",
                        principalTable: "gia_caso_uso",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Incidentes de IA (plan 02 §6.6).");

            migrationBuilder.CreateTable(
                name: "gia_asiento",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    herramienta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usuario_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "@pii"),
                    correo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "@pii"),
                    tipo_cuenta = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    asignado_en = table.Column<DateOnly>(type: "date", nullable: false),
                    retirado_en = table.Column<DateOnly>(type: "date", nullable: true),
                    configuracion_verificada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    repos_autorizados = table.Column<List<string>>(type: "text[]", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gia_asiento", x => x.id);
                    table.CheckConstraint("ck_gia_asiento_cuenta", "tipo_cuenta IN ('corporativa', 'personal_autorizada')");
                    table.ForeignKey(
                        name: "fk_gia_asiento_gia_herramienta_herramienta_id",
                        column: x => x.herramienta_id,
                        principalSchema: "atlas",
                        principalTable: "gia_herramienta",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Asientos de cada herramienta (plan 02 §12.1).");

            migrationBuilder.CreateTable(
                name: "gia_aceptacion_politica",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    politica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aceptada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip = table.Column<IPAddress>(type: "inet", nullable: true, comment: "@pii"),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gia_aceptacion_politica", x => x.id);
                    table.ForeignKey(
                        name: "fk_gia_aceptacion_politica_gia_politica_politica_id",
                        column: x => x.politica_id,
                        principalSchema: "atlas",
                        principalTable: "gia_politica",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Registro de aceptación de cada versión por persona (plan 02 §6).");

            migrationBuilder.CreateTable(
                name: "gia_voto_evaluacion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    voto = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    condiciones = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    comentario = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true, comment: "@pii"),
                    votado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gia_voto_evaluacion", x => x.id);
                    table.CheckConstraint("ck_gia_voto_rol", "rol IN ('responsable_ia', 'opd', 'comite', 'seguridad')");
                    table.CheckConstraint("ck_gia_voto_voto", "voto IN ('aprobar', 'rechazar')");
                    table.ForeignKey(
                        name: "fk_gia_voto_evaluacion_gia_evaluacion_riesgo_evaluacion_id",
                        column: x => x.evaluacion_id,
                        principalSchema: "atlas",
                        principalTable: "gia_evaluacion_riesgo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Votos sobre una evaluación, en la calidad de un rol de gobierno (ATLAS-ADR-0055 §5).");

            migrationBuilder.CreateIndex(
                name: "ix_gia_aceptacion_politica_politica_id_persona_id",
                schema: "atlas",
                table: "gia_aceptacion_politica",
                columns: new[] { "politica_id", "persona_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gia_asiento_herramienta_id",
                schema: "atlas",
                table: "gia_asiento",
                column: "herramienta_id");

            migrationBuilder.CreateIndex(
                name: "ix_gia_caso_uso_codigo",
                schema: "atlas",
                table: "gia_caso_uso",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gia_caso_uso_estado",
                schema: "atlas",
                table: "gia_caso_uso",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_gia_caso_uso_proxima_revision",
                schema: "atlas",
                table: "gia_caso_uso",
                column: "proxima_revision");

            migrationBuilder.CreateIndex(
                name: "ix_gia_caso_uso_version_caso_uso_id_numero",
                schema: "atlas",
                table: "gia_caso_uso_version",
                columns: new[] { "caso_uso_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gia_eipd_caso_uso_id",
                schema: "atlas",
                table: "gia_eipd",
                column: "caso_uso_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gia_evaluacion_riesgo_caso_uso_id_version",
                schema: "atlas",
                table: "gia_evaluacion_riesgo",
                columns: new[] { "caso_uso_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gia_herramienta_codigo",
                schema: "atlas",
                table: "gia_herramienta",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gia_incidente_caso_uso_id",
                schema: "atlas",
                table: "gia_incidente",
                column: "caso_uso_id");

            migrationBuilder.CreateIndex(
                name: "ix_gia_incidente_codigo",
                schema: "atlas",
                table: "gia_incidente",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gia_incidente_detectado_en",
                schema: "atlas",
                table: "gia_incidente",
                column: "detectado_en");

            migrationBuilder.CreateIndex(
                name: "ix_gia_politica_codigo_version",
                schema: "atlas",
                table: "gia_politica",
                columns: new[] { "codigo", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gia_politica_vigente",
                schema: "atlas",
                table: "gia_politica",
                column: "codigo",
                unique: true,
                filter: "estado = 'vigente'");

            migrationBuilder.CreateIndex(
                name: "ix_gia_rol_gobierno_persona_id_rol",
                schema: "atlas",
                table: "gia_rol_gobierno",
                columns: new[] { "persona_id", "rol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gia_voto_evaluacion_evaluacion_id_persona_id_rol",
                schema: "atlas",
                table: "gia_voto_evaluacion",
                columns: new[] { "evaluacion_id", "persona_id", "rol" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION atlas.gia_inmutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'El historial de la ficha es inmutable (ATLAS-ADR-0055).'
                        USING ERRCODE = 'check_violation';
                END $$;
                CREATE TRIGGER gia_caso_uso_version_inmutable BEFORE UPDATE OR DELETE ON atlas.gia_caso_uso_version
                    FOR EACH ROW EXECUTE FUNCTION atlas.gia_inmutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER gia_caso_uso_version_inmutable ON atlas.gia_caso_uso_version;
                DROP FUNCTION atlas.gia_inmutable();
                """);

            migrationBuilder.DropTable(
                name: "gia_aceptacion_politica",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "gia_asiento",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "gia_caso_uso_version",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "gia_eipd",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "gia_incidente",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "gia_rol_gobierno",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "gia_voto_evaluacion",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "gia_politica",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "gia_herramienta",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "gia_evaluacion_riesgo",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "gia_caso_uso",
                schema: "atlas");
        }
    }
}
