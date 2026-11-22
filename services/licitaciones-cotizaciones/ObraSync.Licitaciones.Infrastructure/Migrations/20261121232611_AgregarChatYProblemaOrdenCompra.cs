using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Licitaciones.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarChatYProblemaOrdenCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaProblemaAbierto",
                table: "OrdenesCompra",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoProblemaAbierto",
                table: "OrdenesCompra",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProblemaResueltoEn",
                table: "OrdenesCompra",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProblemaResueltoPor",
                table: "OrdenesCompra",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TieneProblemaAbierto",
                table: "OrdenesCompra",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "LecturasChatOrden",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrdenCompraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UltimaLecturaEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LecturasChatOrden", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LecturasChatOrden_OrdenesCompra_OrdenCompraId",
                        column: x => x.OrdenCompraId,
                        principalTable: "OrdenesCompra",
                        principalColumn: "OrdenCompraId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MensajesChatOrden",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrdenCompraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AutorNombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AutorRol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EsSistema = table.Column<bool>(type: "bit", nullable: false),
                    Texto = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MensajesChatOrden", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MensajesChatOrden_OrdenesCompra_OrdenCompraId",
                        column: x => x.OrdenCompraId,
                        principalTable: "OrdenesCompra",
                        principalColumn: "OrdenCompraId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LecturasChatOrden_OrdenCompraId_UsuarioId",
                table: "LecturasChatOrden",
                columns: new[] { "OrdenCompraId", "UsuarioId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MensajesChatOrden_OrdenCompraId_FechaCreacion",
                table: "MensajesChatOrden",
                columns: new[] { "OrdenCompraId", "FechaCreacion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LecturasChatOrden");

            migrationBuilder.DropTable(
                name: "MensajesChatOrden");

            migrationBuilder.DropColumn(
                name: "FechaProblemaAbierto",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "MotivoProblemaAbierto",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "ProblemaResueltoEn",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "ProblemaResueltoPor",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "TieneProblemaAbierto",
                table: "OrdenesCompra");
        }
    }
}
