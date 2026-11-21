using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Recepcion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarEntregaConProblemaFotosYDatosDespacho : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MotivoProblema",
                table: "Recepciones",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaEstimadaLlegada",
                table: "OrdenesCompra",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObservacionDespacho",
                table: "OrdenesCompra",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FotosRecepcion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecepcionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NombreArchivo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TamanoBytes = table.Column<long>(type: "bigint", nullable: false),
                    Contenido = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FotosRecepcion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FotosRecepcion_Recepciones_RecepcionId",
                        column: x => x.RecepcionId,
                        principalTable: "Recepciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FotosRecepcion_RecepcionId",
                table: "FotosRecepcion",
                column: "RecepcionId");

            migrationBuilder.CreateIndex(
                name: "IX_FotosRecepcion_RecepcionId_Orden",
                table: "FotosRecepcion",
                columns: new[] { "RecepcionId", "Orden" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FotosRecepcion");

            migrationBuilder.DropColumn(
                name: "MotivoProblema",
                table: "Recepciones");

            migrationBuilder.DropColumn(
                name: "FechaEstimadaLlegada",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "ObservacionDespacho",
                table: "OrdenesCompra");
        }
    }
}
