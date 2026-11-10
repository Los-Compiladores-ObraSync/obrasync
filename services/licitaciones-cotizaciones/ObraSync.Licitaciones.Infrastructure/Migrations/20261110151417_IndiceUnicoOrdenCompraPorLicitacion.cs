using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Licitaciones.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IndiceUnicoOrdenCompraPorLicitacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrdenesCompra_LicitacionId",
                table: "OrdenesCompra");

            migrationBuilder.CreateIndex(
                name: "IX_OrdenesCompra_LicitacionId",
                table: "OrdenesCompra",
                column: "LicitacionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrdenesCompra_LicitacionId",
                table: "OrdenesCompra");

            migrationBuilder.CreateIndex(
                name: "IX_OrdenesCompra_LicitacionId",
                table: "OrdenesCompra",
                column: "LicitacionId");
        }
    }
}
