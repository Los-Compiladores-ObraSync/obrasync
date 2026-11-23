using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Licitaciones.Infrastructure.Migrations
{
    [DbContext(typeof(LicitacionesDbContext))]
    [Migration("20261123190000_AgregarRedespachoRemanenteOrdenCompra")]
    public partial class AgregarRedespachoRemanenteOrdenCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NumeroDespacho",
                table: "OrdenesCompra",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "TieneRecepcionParcialPendiente",
                table: "OrdenesCompra",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaUltimaRecepcionParcial",
                table: "OrdenesCompra",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE OrdenesCompra SET NumeroDespacho = 1 WHERE NumeroDespacho = 0 AND (Estado = 'Despachada' OR FechaDespacho IS NOT NULL);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NumeroDespacho",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "TieneRecepcionParcialPendiente",
                table: "OrdenesCompra");

            migrationBuilder.DropColumn(
                name: "FechaUltimaRecepcionParcial",
                table: "OrdenesCompra");
        }
    }
}
