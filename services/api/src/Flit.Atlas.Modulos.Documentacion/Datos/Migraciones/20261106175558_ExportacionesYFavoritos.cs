using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Atlas.Modulos.Documentacion.Datos.Migraciones
{
    /// <inheritdoc />
    public partial class ExportacionesYFavoritos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "documentacion_exportacion",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    documentos = table.Column<string>(type: "jsonb", nullable: false),
                    formatos = table.Column<List<string>>(type: "text[]", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entrada_clave = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    entrada_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    objeto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    hash_contenido = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    archivos = table.Column<string>(type: "jsonb", nullable: false),
                    error_codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    solicitada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    iniciada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    terminada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    vence_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    vencida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentacion_exportacion", x => x.id);
                    table.CheckConstraint("ck_documentacion_exportacion_archivos", "jsonb_typeof(archivos) = 'array'");
                    table.CheckConstraint("ck_documentacion_exportacion_documentos", "jsonb_typeof(documentos) = 'array'");
                    table.CheckConstraint("ck_documentacion_exportacion_hash", "hash_contenido IS NULL OR hash_contenido ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_documentacion_exportacion_lista", "estado <> 'lista' OR (objeto IS NOT NULL AND hash_contenido IS NOT NULL AND terminada_en IS NOT NULL AND vence_en IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_documentacion_exportacion_documentacion_documento_documento",
                        column: x => x.documento_id,
                        principalSchema: "atlas",
                        principalTable: "documentacion_documento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Exportaciones de documentos y colecciones: quién, qué, formatos, estado y objetos en el S3 (ATLAS-ADR-0053).");

            migrationBuilder.CreateTable(
                name: "documentacion_favorito",
                schema: "atlas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentacion_favorito", x => x.id);
                    table.ForeignKey(
                        name: "fk_documentacion_favorito_documentacion_documento_documento_id",
                        column: x => x.documento_id,
                        principalSchema: "atlas",
                        principalTable: "documentacion_documento",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Documentos que cada persona marcó para leer sin conexión (ATLAS-ADR-0034).");

            migrationBuilder.CreateIndex(
                name: "ix_documentacion_exportacion_documento_id",
                schema: "atlas",
                table: "documentacion_exportacion",
                column: "documento_id");

            migrationBuilder.CreateIndex(
                name: "ix_documentacion_exportacion_estado_vence_en",
                schema: "atlas",
                table: "documentacion_exportacion",
                columns: new[] { "estado", "vence_en" });

            migrationBuilder.CreateIndex(
                name: "ix_documentacion_exportacion_solicitada_por_solicitada_en",
                schema: "atlas",
                table: "documentacion_exportacion",
                columns: new[] { "solicitada_por", "solicitada_en" });

            migrationBuilder.CreateIndex(
                name: "ix_documentacion_favorito_documento_id",
                schema: "atlas",
                table: "documentacion_favorito",
                column: "documento_id");

            migrationBuilder.CreateIndex(
                name: "ix_documentacion_favorito_persona_id_documento_id",
                schema: "atlas",
                table: "documentacion_favorito",
                columns: new[] { "persona_id", "documento_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documentacion_exportacion",
                schema: "atlas");

            migrationBuilder.DropTable(
                name: "documentacion_favorito",
                schema: "atlas");
        }
    }
}
