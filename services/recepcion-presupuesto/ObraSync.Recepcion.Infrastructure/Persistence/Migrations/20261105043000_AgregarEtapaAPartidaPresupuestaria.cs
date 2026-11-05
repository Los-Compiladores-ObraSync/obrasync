using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Recepcion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarEtapaAPartidaPresupuestaria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Etapa",
                table: "PartidasPresupuestarias",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Etapa",
                table: "PartidasPresupuestarias");
        }
    }
}
