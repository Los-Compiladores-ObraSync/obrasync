using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ObraSync.Requisiciones.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Insumos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    UnidadMedida = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Moneda = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: "ARS"),
                    PrecioReferencia = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Insumos", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Insumos",
                columns: new[] { "Id", "Activo", "Codigo", "Descripcion", "Moneda", "PrecioReferencia", "UnidadMedida" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111110001"), true, "MAT-CEM-001", "Cemento Portland Normal (bolsa 50 kg)", "ARS", 8500.00m, "BOLSA" },
                    { new Guid("11111111-1111-1111-1111-111111110002"), true, "MAT-CEM-002", "Cemento de Albañilería Plasticor (bolsa 40 kg)", "ARS", 7200.00m, "BOLSA" },
                    { new Guid("11111111-1111-1111-1111-111111110003"), true, "MAT-CAL-001", "Cal Hidratada en Polvo (bolsa 25 kg)", "ARS", 4800.00m, "BOLSA" },
                    { new Guid("11111111-1111-1111-1111-111111110004"), true, "MAT-ARI-001", "Arena Gruesa Lavada de Río", "ARS", 28000.00m, "M3" },
                    { new Guid("11111111-1111-1111-1111-111111110005"), true, "MAT-ARI-002", "Piedra Partida Granítica 6-20 mm", "ARS", 42000.00m, "M3" },
                    { new Guid("11111111-1111-1111-1111-111111110006"), true, "MAT-ARI-003", "Cascote Picado Limpio de Ladrillo", "ARS", 19500.00m, "M3" },
                    { new Guid("11111111-1111-1111-1111-111111110007"), true, "MAT-HIE-001", "Hierro Conformado ADN 420 Ø 8 mm (barra 12 m)", "ARS", 9600.00m, "UNIDAD" },
                    { new Guid("11111111-1111-1111-1111-111111110008"), true, "MAT-HIE-002", "Hierro Conformado ADN 420 Ø 10 mm (barra 12 m)", "ARS", 14800.00m, "UNIDAD" },
                    { new Guid("11111111-1111-1111-1111-111111110009"), true, "MAT-HIE-003", "Hierro Conformado ADN 420 Ø 12 mm (barra 12 m)", "ARS", 21200.00m, "UNIDAD" },
                    { new Guid("11111111-1111-1111-1111-111111110010"), true, "MAT-MAL-001", "Malla Electrosoldada Standard Q188 15x15 cm 6 mm (panel 5x2.4 m)", "ARS", 68500.00m, "UNIDAD" },
                    { new Guid("11111111-1111-1111-1111-111111110011"), true, "MAT-ALA-001", "Alambre Negro Recocido de Atar Nº 16", "ARS", 3400.00m, "KG" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Insumos_Codigo",
                table: "Insumos",
                column: "Codigo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Insumos");
        }
    }
}
