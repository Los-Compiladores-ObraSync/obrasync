using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Recepcion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EvitarRecepcionesDuplicadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Recepciones_OrdenCompraId",
                table: "Recepciones");

            migrationBuilder.CreateIndex(
                name: "IX_Recepciones_OrdenCompraId",
                table: "Recepciones",
                column: "OrdenCompraId",
                unique: true,
                filter: "[Estado] = 3");

            migrationBuilder.CreateIndex(
                name: "IX_Recepciones_OrdenCompraId_NumeroRemito",
                table: "Recepciones",
                columns: new[] { "OrdenCompraId", "NumeroRemito" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Recepciones_OrdenCompraId",
                table: "Recepciones");

            migrationBuilder.DropIndex(
                name: "IX_Recepciones_OrdenCompraId_NumeroRemito",
                table: "Recepciones");

            migrationBuilder.CreateIndex(
                name: "IX_Recepciones_OrdenCompraId",
                table: "Recepciones",
                column: "OrdenCompraId");
        }
    }
}
