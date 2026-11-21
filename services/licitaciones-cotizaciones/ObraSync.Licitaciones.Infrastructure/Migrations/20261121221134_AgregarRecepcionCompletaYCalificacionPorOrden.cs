using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Licitaciones.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarRecepcionCompletaYCalificacionPorOrden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CalificacionesProveedores_ProveedorId_CalificadorId",
                table: "CalificacionesProveedores");

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaRecepcionCompleta",
                table: "OrdenesCompra",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RecibidaCompleta",
                table: "OrdenesCompra",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Comentario",
                table: "CalificacionesProveedores",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrdenCompraId",
                table: "CalificacionesProveedores",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalificacionesProveedores_OrdenCompraId_CalificadorId",
                table: "CalificacionesProveedores",
                columns: new[] { "OrdenCompraId", "CalificadorId" },
                unique: true,
                filter: "[OrdenCompraId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CalificacionesProveedores_ProveedorId",
                table: "CalificacionesProveedores",
                column: "ProveedorId");

            migrationBuilder.CreateIndex(
                name: "IX_CalificacionesProveedores_ProveedorId_CalificadorId",
                table: "CalificacionesProveedores",
                columns: new[] { "ProveedorId", "CalificadorId" },
                unique: true,
                filter: "[OrdenCompraId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CalificacionesProveedores_OrdenCompraId_CalificadorId",
                table: "CalificacionesProveedores");

            migrationBuilder.DropIndex(
                name: "IX_CalificacionesProveedores_ProveedorId",
                table: "CalificacionesProveedores");

            migrationBuilder.DropIndex(
                name: "IX_CalificacionesProveedores_ProveedorId_CalificadorId",
                table: "CalificacionesProveedores");

            migrationBuilder.DropColumn(
                name: "FechaRecepcionCompleta",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "RecibidaCompleta",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "Comentario",
                table: "CalificacionesProveedores");

            migrationBuilder.DropColumn(
                name: "OrdenCompraId",
                table: "CalificacionesProveedores");

            migrationBuilder.CreateIndex(
                name: "IX_CalificacionesProveedores_ProveedorId_CalificadorId",
                table: "CalificacionesProveedores",
                columns: new[] { "ProveedorId", "CalificadorId" },
                unique: true);
        }
    }
}
