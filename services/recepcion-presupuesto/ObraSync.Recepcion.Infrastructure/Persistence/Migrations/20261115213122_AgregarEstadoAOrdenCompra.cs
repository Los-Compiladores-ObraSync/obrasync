using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Recepcion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarEstadoAOrdenCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Estado",
                table: "OrdenesCompra",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "MotivoBloqueo",
                table: "OrdenesCompra",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Estado",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "MotivoBloqueo",
                table: "OrdenesCompra");
        }
    }
}
