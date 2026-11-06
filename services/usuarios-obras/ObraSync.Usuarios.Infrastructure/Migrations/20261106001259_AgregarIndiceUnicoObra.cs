using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Usuarios.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarIndiceUnicoObra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Obras_Nombre_Ubicacion",
                table: "Obras",
                columns: new[] { "Nombre", "Ubicacion" },
                unique: true,
                filter: "[Activa] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Obras_Nombre_Ubicacion",
                table: "Obras");
        }
    }
}
