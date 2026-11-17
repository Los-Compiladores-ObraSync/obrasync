using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Recepcion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarFechaBloqueoAOrdenCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaBloqueo",
                table: "OrdenesCompra",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdenesCompra_Estado",
                table: "OrdenesCompra",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_OrdenesCompra_FechaBloqueo",
                table: "OrdenesCompra",
                column: "FechaBloqueo");

            // Backfill de fecha de bloqueo para órdenes que ya estaban en estado Bloqueada (#210).
            migrationBuilder.Sql("""
                UPDATE OrdenesCompra
                SET FechaBloqueo = COALESCE(UpdatedAt, CreatedAt)
                WHERE Estado = 2 AND FechaBloqueo IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrdenesCompra_Estado",
                table: "OrdenesCompra");

            migrationBuilder.DropIndex(
                name: "IX_OrdenesCompra_FechaBloqueo",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "FechaBloqueo",
                table: "OrdenesCompra");
        }
    }
}
