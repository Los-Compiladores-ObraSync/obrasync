using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Requisiciones.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarRechazoRequisicion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaRechazo",
                table: "Requisiciones",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoRechazo",
                table: "Requisiciones",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RechazadorId",
                table: "Requisiciones",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FechaRechazo",
                table: "Requisiciones");

            migrationBuilder.DropColumn(
                name: "MotivoRechazo",
                table: "Requisiciones");

            migrationBuilder.DropColumn(
                name: "RechazadorId",
                table: "Requisiciones");
        }
    }
}
