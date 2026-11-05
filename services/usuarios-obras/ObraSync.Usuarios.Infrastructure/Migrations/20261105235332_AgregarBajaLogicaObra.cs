using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Usuarios.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarBajaLogicaObra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Activa",
                table: "Obras",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Activa",
                table: "Obras");
        }
    }
}
