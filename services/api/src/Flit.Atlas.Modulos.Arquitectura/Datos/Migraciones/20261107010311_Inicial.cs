using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Arquitectura.Datos.Migraciones
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
                name: "arquitectura_comparacion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ambiente = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    comparada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arquitectura_comparacion", x => x.id);
                },
                comment: "Última comparación del modelo con gitops por ambiente (ATLAS-ADR-0056).");

            migrationBuilder.CreateTable(
                name: "arquitectura_hallazgo_drift",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    capa = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ambiente = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    cluster = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    codigo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    elemento_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    sujeto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    severidad = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    visibilidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    aplicativo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    detectado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resuelto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    alertado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arquitectura_hallazgo_drift", x => x.id);
                },
                comment: "Hallazgos de drift entre modelo, gitops y Argo CD; sin hosts ni IP (ATLAS-ADR-0056).");

            migrationBuilder.CreateTable(
                name: "arquitectura_lectura_argo",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cluster = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    motivo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    leida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arquitectura_lectura_argo", x => x.id);
                },
                comment: "Última lectura de Argo CD por clúster: resultado, nunca credenciales (ATLAS-ADR-0056).");

            migrationBuilder.CreateTable(
                name: "arquitectura_proyecto",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    aplicativo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repositorio = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arquitectura_proyecto", x => x.id);
                },
                comment: "Proyectos LikeC4 y fuentes de snapshots; cada uno lo publica su repo (ATLAS-ADR-0056).");

            migrationBuilder.CreateTable(
                name: "arquitectura_publicacion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proyecto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    canal = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    cluster = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    rama = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    commit_sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version_propuesta = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    visibilidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    herramienta_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    contenido = table.Column<string>(type: "text", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    workflow = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    workflow_del_job = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ejecucion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    publicado_por = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    publicado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    generado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arquitectura_publicacion", x => x.id);
                    table.CheckConstraint("ck_arquitectura_publicacion_cluster", "(tipo = 'modelo') = (cluster = '')");
                    table.CheckConstraint("ck_arquitectura_publicacion_sha256", "sha256 ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_arquitectura_publicacion_arquitectura_proyecto_proyecto_id",
                        column: x => x.proyecto_id,
                        principalSchema: "atlas",
                        principalTable: "arquitectura_proyecto",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Versiones inmutables por sha y canal: modelos LikeC4 y snapshots de despliegue (ATLAS-ADR-0056).");

            migrationBuilder.CreateTable(
                name: "arquitectura_elemento",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    publicacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    elemento_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    titulo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tecnologia = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    etiquetas = table.Column<List<string>>(type: "text[]", nullable: false),
                    metadatos = table.Column<string>(type: "jsonb", nullable: false),
                    enlaces = table.Column<string>(type: "jsonb", nullable: false),
                    k8s_workload = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arquitectura_elemento", x => x.id);
                    table.ForeignKey(
                        name: "fk_arquitectura_elemento_arquitectura_publicacion_publicacion_",
                        column: x => x.publicacion_id,
                        principalSchema: "atlas",
                        principalTable: "arquitectura_publicacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Elementos por publicación: proyección del JSON para la ficha y el drift (ATLAS-ADR-0056).");

            migrationBuilder.CreateTable(
                name: "arquitectura_vista",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    publicacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vista_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    categoria = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    titulo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arquitectura_vista", x => x.id);
                    table.ForeignKey(
                        name: "fk_arquitectura_vista_arquitectura_publicacion_publicacion_id",
                        column: x => x.publicacion_id,
                        principalSchema: "atlas",
                        principalTable: "arquitectura_publicacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Vistas por publicación (ATLAS-ADR-0056).");

            migrationBuilder.CreateIndex(
                name: "ix_arquitectura_comparacion_ambiente",
                schema: "atlas",
                table: "arquitectura_comparacion",
                column: "ambiente",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_arquitectura_elemento_publicacion_id_elemento_id",
                schema: "atlas",
                table: "arquitectura_elemento",
                columns: new[] { "publicacion_id", "elemento_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_arquitectura_hallazgo_drift_ambiente_estado",
                schema: "atlas",
                table: "arquitectura_hallazgo_drift",
                columns: new[] { "ambiente", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_arquitectura_hallazgo_drift_clave",
                schema: "atlas",
                table: "arquitectura_hallazgo_drift",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_arquitectura_lectura_argo_cluster",
                schema: "atlas",
                table: "arquitectura_lectura_argo",
                column: "cluster",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_arquitectura_proyecto_codigo",
                schema: "atlas",
                table: "arquitectura_proyecto",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_arquitectura_publicacion_proyecto_id_tipo_canal_cluster_com",
                schema: "atlas",
                table: "arquitectura_publicacion",
                columns: new[] { "proyecto_id", "tipo", "canal", "cluster", "commit_sha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_arquitectura_publicacion_tipo_canal_cluster_publicado_en",
                schema: "atlas",
                table: "arquitectura_publicacion",
                columns: new[] { "tipo", "canal", "cluster", "publicado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_arquitectura_vista_publicacion_id_vista_id",
                schema: "atlas",
                table: "arquitectura_vista",
                columns: new[] { "publicacion_id", "vista_id" },
                unique: true);
        
            migrationBuilder.Sql("""
                CREATE FUNCTION atlas.arquitectura_inmutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Las publicaciones de arquitectura son inmutables (ATLAS-ADR-0056).'
                        USING ERRCODE = 'check_violation';
                END $$;
                CREATE TRIGGER arquitectura_publicacion_inmutable BEFORE UPDATE OR DELETE ON atlas.arquitectura_publicacion
                    FOR EACH ROW EXECUTE FUNCTION atlas.arquitectura_inmutable();
                CREATE TRIGGER arquitectura_elemento_inmutable BEFORE UPDATE OR DELETE ON atlas.arquitectura_elemento
                    FOR EACH ROW EXECUTE FUNCTION atlas.arquitectura_inmutable();
                CREATE TRIGGER arquitectura_vista_inmutable BEFORE UPDATE OR DELETE ON atlas.arquitectura_vista
                    FOR EACH ROW EXECUTE FUNCTION atlas.arquitectura_inmutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER arquitectura_vista_inmutable ON atlas.arquitectura_vista;
                DROP TRIGGER arquitectura_elemento_inmutable ON atlas.arquitectura_elemento;
                DROP TRIGGER arquitectura_publicacion_inmutable ON atlas.arquitectura_publicacion;
                DROP FUNCTION atlas.arquitectura_inmutable();
                """);

            migrationBuilder.DropTable(
                name: "arquitectura_comparacion",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "arquitectura_elemento",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "arquitectura_hallazgo_drift",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "arquitectura_lectura_argo",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "arquitectura_vista",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "arquitectura_publicacion",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "arquitectura_proyecto",
                schema: "atlas");
        }
    }
}
