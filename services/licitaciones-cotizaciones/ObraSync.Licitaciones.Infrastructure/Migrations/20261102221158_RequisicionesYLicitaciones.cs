using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Licitaciones.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RequisicionesYLicitaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Licitaciones",
                columns: table => new
                {
                    LicitacionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequisicionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NumeroRequisicion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ObraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaLimiteCotizacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Licitaciones", x => x.LicitacionId);
                });

            migrationBuilder.CreateTable(
                name: "RequisicionesAprobadas",
                columns: table => new
                {
                    RequisicionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NumeroRequisicion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ObraId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecibidaEnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequisicionesAprobadas", x => x.RequisicionId);
                });

            migrationBuilder.CreateTable(
                name: "LicitacionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicitacionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArticuloId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnidadMedida = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicitacionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicitacionItems_Licitaciones_LicitacionId",
                        column: x => x.LicitacionId,
                        principalTable: "Licitaciones",
                        principalColumn: "LicitacionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RequisicionAprobadaItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequisicionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArticuloId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnidadMedida = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequisicionAprobadaItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequisicionAprobadaItems_RequisicionesAprobadas_RequisicionId",
                        column: x => x.RequisicionId,
                        principalTable: "RequisicionesAprobadas",
                        principalColumn: "RequisicionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Licitaciones_RequisicionId",
                table: "Licitaciones",
                column: "RequisicionId");

            migrationBuilder.CreateIndex(
                name: "IX_LicitacionItems_LicitacionId",
                table: "LicitacionItems",
                column: "LicitacionId");

            migrationBuilder.CreateIndex(
                name: "IX_RequisicionAprobadaItems_RequisicionId",
                table: "RequisicionAprobadaItems",
                column: "RequisicionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LicitacionItems");

            migrationBuilder.DropTable(
                name: "RequisicionAprobadaItems");

            migrationBuilder.DropTable(
                name: "Licitaciones");

            migrationBuilder.DropTable(
                name: "RequisicionesAprobadas");
        }
    }
}
