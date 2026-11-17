using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Licitaciones.Infrastructure.Migrations
{
    /// <summary>
    /// #206: la cotización guarda el nombre del proveedor tal como figuraba al cotizar, y el Magic Link guarda el que escribió
    /// quien lo generó (no tiene JWT). Las dos columnas aceptan null: las cotizaciones y enlaces que ya existen quedan sin nombre.
    /// </summary>
    [DbContext(typeof(LicitacionesDbContext))]
    [Migration("20261117040000_AgregarNombreProveedor")]
    public partial class AgregarNombreProveedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProveedorNombre",
                table: "Cotizaciones",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProveedorNombre",
                table: "MagicLinks",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProveedorNombre",
                table: "Cotizaciones");

            migrationBuilder.DropColumn(
                name: "ProveedorNombre",
                table: "MagicLinks");
        }
    }
}
