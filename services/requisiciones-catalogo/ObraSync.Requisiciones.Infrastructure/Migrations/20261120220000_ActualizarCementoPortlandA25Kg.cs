using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ObraSync.Requisiciones.Infrastructure.Persistence;

#nullable disable

namespace ObraSync.Requisiciones.Infrastructure.Migrations
{
    // Las bolsas de cemento (Portland 50 kg y Plasticor 40 kg) pasan a 25 kg, el contenido neto máximo vigente.
    // Solo cambia la descripción de los insumos sembrados: el modelo de datos no cambia,
    // por eso el snapshot solo actualiza el texto de la semilla.
    [DbContext(typeof(RequisicionesDbContext))]
    [Migration("20261120220000_ActualizarCementoPortlandA25Kg")]
    public partial class ActualizarCementoPortlandA25Kg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Insumos",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111110001"),
                column: "Descripcion",
                value: "Cemento Portland Normal (bolsa 25 kg)");

            migrationBuilder.UpdateData(
                table: "Insumos",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111110002"),
                column: "Descripcion",
                value: "Cemento de Albañilería Plasticor (bolsa 25 kg)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Insumos",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111110001"),
                column: "Descripcion",
                value: "Cemento Portland Normal (bolsa 50 kg)");

            migrationBuilder.UpdateData(
                table: "Insumos",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111110002"),
                column: "Descripcion",
                value: "Cemento de Albañilería Plasticor (bolsa 40 kg)");
        }
    }
}
