using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Requisiciones.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RequisicionesMateriales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Requisiciones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NumeroRequisicion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ObraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Etapa = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SolicitanteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AprobadorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pendiente"),
                    Observaciones = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaAprobacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Requisiciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequisicionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequisicionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArticuloId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnidadMedida = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequisicionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequisicionItems_Requisiciones_RequisicionId",
                        column: x => x.RequisicionId,
                        principalTable: "Requisiciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Requisiciones_NumeroRequisicion",
                table: "Requisiciones",
                column: "NumeroRequisicion",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Requisiciones_ObraId",
                table: "Requisiciones",
                column: "ObraId");

            migrationBuilder.CreateIndex(
                name: "IX_RequisicionItems_RequisicionId",
                table: "RequisicionItems",
                column: "RequisicionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequisicionItems");

            migrationBuilder.DropTable(
                name: "Requisiciones");
        }
    }
}
