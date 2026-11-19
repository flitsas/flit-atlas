using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Plataforma.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class RolesYPermisos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "tipo",
                schema: "atlas",
                table: "plataforma_rol",
                type: "character varying(9)",
                maxLength: 9,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7);

            migrationBuilder.AlterColumn<string>(
                name: "alcance_tipo",
                schema: "atlas",
                table: "plataforma_asignacion_rol",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(12)",
                oldMaxLength: 12);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "vencimiento_procesado_en",
                schema: "atlas",
                table: "plataforma_asignacion_rol",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "plataforma_permiso",
                schema: "atlas",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    modulo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_permiso", x => x.codigo);
                },
                comment: "Proyección de los manifiestos de permisos del código; la escribe el migrador.");

            migrationBuilder.CreateTable(
                name: "plataforma_rol_permiso",
                schema: "atlas",
                columns: table => new
                {
                    rol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permiso_codigo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plataforma_rol_permiso", x => new { x.rol_id, x.permiso_codigo });
                    table.ForeignKey(
                        name: "fk_plataforma_rol_permiso_plataforma_permiso_permiso_codigo",
                        column: x => x.permiso_codigo,
                        principalSchema: "atlas",
                        principalTable: "plataforma_permiso",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_plataforma_rol_permiso_plataforma_rol_rol_id",
                        column: x => x.rol_id,
                        principalSchema: "atlas",
                        principalTable: "plataforma_rol",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Proyección de la matriz rol × permiso del código; la escribe el migrador.");

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_asignacion_rol_vigente_hasta",
                schema: "atlas",
                table: "plataforma_asignacion_rol",
                column: "vigente_hasta",
                filter: "vigente_hasta IS NOT NULL AND vencimiento_procesado_en IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_plataforma_rol_permiso_permiso_codigo",
                schema: "atlas",
                table: "plataforma_rol_permiso",
                column: "permiso_codigo");

            // RLS en las tablas con datos de organizaciones (prompt maestro §9 y §12, ATLAS-ADR-0023), como defensa en
            // profundidad bajo el predicado obligatorio de EF Core. Las variables las fija InterceptorAlcanceDatos al
            // abrir cada conexión; sin ellas no se ve ninguna fila. FORCE porque flit_atlas es el dueño de las tablas y,
            // sin FORCE, el dueño se salta RLS. (Un superusuario o un rol BYPASSRLS siempre se la salta.)
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION atlas.plataforma_organizacion_visible(organizacion uuid) RETURNS boolean
                LANGUAGE sql STABLE PARALLEL SAFE AS $fn$
                    SELECT coalesce(current_setting('atlas.alcance_total', true), '') = 'on'
                        OR (organizacion IS NOT NULL
                            AND organizacion = ANY (string_to_array(nullif(current_setting('atlas.organizaciones', true), ''), ',')::uuid[]));
                $fn$;
                COMMENT ON FUNCTION atlas.plataforma_organizacion_visible(uuid) IS
                    'RLS: ¿la sesión (atlas.alcance_total, atlas.organizaciones) puede ver filas de esta organización? ATLAS-ADR-0023.';
                """);
            foreach (var (tabla, columna) in TablasConOrganizacion)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE atlas.{tabla} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE atlas.{tabla} FORCE ROW LEVEL SECURITY;
                    CREATE POLICY organizacion_visible ON atlas.{tabla}
                        USING (atlas.plataforma_organizacion_visible({columna}))
                        WITH CHECK (atlas.plataforma_organizacion_visible({columna}));
                    """);
            }
            // La auditoría es de solo inserción: se lee con el alcance, pero se escribe siempre (un evento de un externo o
            // sin organización también se registra).
            migrationBuilder.Sql("""
                ALTER TABLE atlas.plataforma_auditoria ENABLE ROW LEVEL SECURITY;
                ALTER TABLE atlas.plataforma_auditoria FORCE ROW LEVEL SECURITY;
                CREATE POLICY organizacion_visible ON atlas.plataforma_auditoria FOR SELECT
                    USING (atlas.plataforma_organizacion_visible(organizacion_id));
                CREATE POLICY insercion ON atlas.plataforma_auditoria FOR INSERT WITH CHECK (true);
                """);
        }

        /// <summary>Tablas con RLS por organización y la columna que la indica.</summary>
        private static readonly (string Tabla, string Columna)[] TablasConOrganizacion =
        [
            ("plataforma_organizacion", "id"),
            ("plataforma_persona", "organizacion_id"),
            ("plataforma_asignacion_rol", "organizacion_id"),
            ("plataforma_invitacion", "organizacion_id"),
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS insercion ON atlas.plataforma_auditoria;
                DROP POLICY IF EXISTS organizacion_visible ON atlas.plataforma_auditoria;
                ALTER TABLE atlas.plataforma_auditoria NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE atlas.plataforma_auditoria DISABLE ROW LEVEL SECURITY;
                """);
            foreach (var (tabla, _) in TablasConOrganizacion)
            {
                migrationBuilder.Sql($"""
                    DROP POLICY IF EXISTS organizacion_visible ON atlas.{tabla};
                    ALTER TABLE atlas.{tabla} NO FORCE ROW LEVEL SECURITY;
                    ALTER TABLE atlas.{tabla} DISABLE ROW LEVEL SECURITY;
                    """);
            }
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS atlas.plataforma_organizacion_visible(uuid);");

            migrationBuilder.DropTable(
                name: "plataforma_rol_permiso",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "plataforma_permiso",
                schema: "atlas");

            migrationBuilder.DropIndex(
                name: "ix_plataforma_asignacion_rol_vigente_hasta",
                schema: "atlas",
                table: "plataforma_asignacion_rol");

            migrationBuilder.DropColumn(
                name: "vencimiento_procesado_en",
                schema: "atlas",
                table: "plataforma_asignacion_rol");

            migrationBuilder.AlterColumn<string>(
                name: "tipo",
                schema: "atlas",
                table: "plataforma_rol",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(9)",
                oldMaxLength: 9);

            migrationBuilder.AlterColumn<string>(
                name: "alcance_tipo",
                schema: "atlas",
                table: "plataforma_asignacion_rol",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);
        }
    }
}
