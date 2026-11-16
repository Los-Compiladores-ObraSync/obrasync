using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Recepcion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarSinPartidaAOrdenCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SinPartidaPresupuestaria",
                table: "OrdenesCompra",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_OrdenesCompra_SinPartidaPresupuestaria",
                table: "OrdenesCompra",
                column: "SinPartidaPresupuestaria");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrdenesCompra_SinPartidaPresupuestaria",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "SinPartidaPresupuestaria",
                table: "OrdenesCompra");
        }
    }
}
