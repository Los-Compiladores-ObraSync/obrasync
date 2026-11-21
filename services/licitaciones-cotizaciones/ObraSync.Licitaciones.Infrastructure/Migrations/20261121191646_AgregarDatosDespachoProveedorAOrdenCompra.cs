using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Licitaciones.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarDatosDespachoProveedorAOrdenCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DespachadaPor",
                table: "OrdenesCompra",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaEstimadaLlegada",
                table: "OrdenesCompra",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Observacion",
                table: "OrdenesCompra",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE OrdenesCompra
                SET DespachadaPor = ProveedorId
                WHERE Estado = 'Despachada'
                  AND DespachadaPor IS NULL
                  AND ProveedorId <> '00000000-0000-0000-0000-000000000000';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DespachadaPor",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "FechaEstimadaLlegada",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "Observacion",
                table: "OrdenesCompra");
        }
    }
}
