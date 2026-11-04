using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Usuarios.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarPlantillasObra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlantillasObra",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlantillasObra", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EtapasPlantilla",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NombreEtapa = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    PlantillaObraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EtapasPlantilla", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EtapasPlantilla_PlantillasObra_PlantillaObraId",
                        column: x => x.PlantillaObraId,
                        principalTable: "PlantillasObra",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EtapasPlantilla_PlantillaObraId",
                table: "EtapasPlantilla",
                column: "PlantillaObraId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EtapasPlantilla");

            migrationBuilder.DropTable(
                name: "PlantillasObra");
        }
    }
}
