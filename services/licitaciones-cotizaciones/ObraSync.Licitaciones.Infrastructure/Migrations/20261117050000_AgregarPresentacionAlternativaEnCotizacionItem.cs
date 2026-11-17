using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Licitaciones.Infrastructure.Migrations
{
    [DbContext(typeof(LicitacionesDbContext))]
    [Migration("20261117050000_AgregarPresentacionAlternativaEnCotizacionItem")]
    public partial class AgregarPresentacionAlternativaEnCotizacionItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PresentacionOfrecida",
                table: "CotizacionItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CantidadOfrecida",
                table: "CotizacionItems",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CoberturaParcial",
                table: "CotizacionItems",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PresentacionOfrecida",
                table: "CotizacionItems");

            migrationBuilder.DropColumn(
                name: "CantidadOfrecida",
                table: "CotizacionItems");

            migrationBuilder.DropColumn(
                name: "CoberturaParcial",
                table: "CotizacionItems");
        }
    }
}
