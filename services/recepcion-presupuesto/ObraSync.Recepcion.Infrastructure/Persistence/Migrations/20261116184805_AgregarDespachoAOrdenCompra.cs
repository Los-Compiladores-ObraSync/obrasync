using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Recepcion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarDespachoAOrdenCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaDespacho",
                table: "OrdenesCompra",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdenesCompra_FechaDespacho",
                table: "OrdenesCompra",
                column: "FechaDespacho");

            // Decisión #201 / #204: las órdenes existentes en la base quedan marcadas
            // como despachadas (FechaDespacho = CreatedAt) para no romper flujos previos.
            migrationBuilder.Sql("""
                UPDATE OrdenesCompra
                SET FechaDespacho = CreatedAt
                WHERE FechaDespacho IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrdenesCompra_FechaDespacho",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "FechaDespacho",
                table: "OrdenesCompra");
        }
    }
}
