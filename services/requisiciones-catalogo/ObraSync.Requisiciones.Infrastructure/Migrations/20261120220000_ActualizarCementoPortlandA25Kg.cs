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
            // SQL directo a propósito: esta migración se escribió a mano y no tiene el modelo (.Designer),
            // y UpdateData lo necesita para conocer los tipos de las columnas (falla con "no entity type mapped").
            migrationBuilder.Sql(
                "UPDATE [Insumos] SET [Descripcion] = N'Cemento Portland Normal (bolsa 25 kg)' " +
                "WHERE [Id] = '11111111-1111-1111-1111-111111110001';");

            migrationBuilder.Sql(
                "UPDATE [Insumos] SET [Descripcion] = N'Cemento de Albañilería Plasticor (bolsa 25 kg)' " +
                "WHERE [Id] = '11111111-1111-1111-1111-111111110002';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE [Insumos] SET [Descripcion] = N'Cemento Portland Normal (bolsa 50 kg)' " +
                "WHERE [Id] = '11111111-1111-1111-1111-111111110001';");

            migrationBuilder.Sql(
                "UPDATE [Insumos] SET [Descripcion] = N'Cemento de Albañilería Plasticor (bolsa 40 kg)' " +
                "WHERE [Id] = '11111111-1111-1111-1111-111111110002';");
        }
    }
}
