using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Recepcion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarAuditoriaLiberacionAOrdenCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LiberadaEn",
                table: "OrdenesCompra",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LiberadaPorUsuarioId",
                table: "OrdenesCompra",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoLiberacion",
                table: "OrdenesCompra",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LiberadaEn",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "LiberadaPorUsuarioId",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "MotivoLiberacion",
                table: "OrdenesCompra");
        }
    }
}
