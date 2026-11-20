using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Licitaciones.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CalificacionPorUsuarioYPromedio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CalificacionesProveedores_ProveedorId",
                table: "CalificacionesProveedores");

            migrationBuilder.AddColumn<Guid>(
                name: "CalificadorId",
                table: "CalificacionesProveedores",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_CalificacionesProveedores_ProveedorId_CalificadorId",
                table: "CalificacionesProveedores",
                columns: new[] { "ProveedorId", "CalificadorId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CalificacionesProveedores_ProveedorId_CalificadorId",
                table: "CalificacionesProveedores");

            migrationBuilder.DropColumn(
                name: "CalificadorId",
                table: "CalificacionesProveedores");

            migrationBuilder.CreateIndex(
                name: "IX_CalificacionesProveedores_ProveedorId",
                table: "CalificacionesProveedores",
                column: "ProveedorId",
                unique: true);
        }
    }
}
