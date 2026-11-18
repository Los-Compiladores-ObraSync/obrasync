using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Licitaciones.Infrastructure.Migrations
{
    [DbContext(typeof(LicitacionesDbContext))]
    [Migration("20261118010000_AgregarDespachoEnOrdenCompra")]
    public partial class AgregarDespachoEnOrdenCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaDespacho",
                table: "OrdenesCompra",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE OrdenesCompra SET Estado = 'Despachada', FechaDespacho = FechaEmision WHERE Estado = 'Emitida';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FechaDespacho",
                table: "OrdenesCompra");
        }
    }
}
