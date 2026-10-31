using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Requisiciones.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregarIdempotencyKeyARequisiciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Requisiciones",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Requisiciones_SolicitanteId_IdempotencyKey",
                table: "Requisiciones",
                columns: new[] { "SolicitanteId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Requisiciones_SolicitanteId_IdempotencyKey",
                table: "Requisiciones");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Requisiciones");
        }
    }
}
