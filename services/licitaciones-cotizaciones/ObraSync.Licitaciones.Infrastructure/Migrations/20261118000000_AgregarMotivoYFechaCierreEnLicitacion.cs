using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Licitaciones.Infrastructure.Migrations
{
    [DbContext(typeof(LicitacionesDbContext))]
    [Migration("20261118000000_AgregarMotivoYFechaCierreEnLicitacion")]
    public partial class AgregarMotivoYFechaCierreEnLicitacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MotivoCierre",
                table: "Licitaciones",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaCierre",
                table: "Licitaciones",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MotivoCierre",
                table: "Licitaciones");

            migrationBuilder.DropColumn(
                name: "FechaCierre",
                table: "Licitaciones");
        }
    }
}
