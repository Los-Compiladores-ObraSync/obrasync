using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Usuarios.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarObrasYEtapas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Obras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Ubicacion = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    FechaInicio = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CapatazId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Obras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Obras_Usuarios_CapatazId",
                        column: x => x.CapatazId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EtapasObras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NombreEtapa = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Pendiente"),
                    ObraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EtapasObras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EtapasObras_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EtapasObras_ObraId",
                table: "EtapasObras",
                column: "ObraId");

            migrationBuilder.CreateIndex(
                name: "IX_Obras_CapatazId",
                table: "Obras",
                column: "CapatazId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EtapasObras");

            migrationBuilder.DropTable(
                name: "Obras");
        }
    }
}
