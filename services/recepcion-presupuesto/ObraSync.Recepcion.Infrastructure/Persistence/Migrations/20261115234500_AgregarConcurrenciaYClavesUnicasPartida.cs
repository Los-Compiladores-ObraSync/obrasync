using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObraSync.Recepcion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarConcurrenciaYClavesUnicasPartida : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "PartidasPresupuestarias",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            // Limpieza defensiva de eventuales filas duplicadas previas a la restricción única (#195),
            // conservando la partida más reciente por (ObraId, CodigoPartida) y por (ObraId, Etapa).
            migrationBuilder.Sql(@"
                ;WITH DuplicadosCodigo AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY ObraId, CodigoPartida ORDER BY CreatedAt DESC, Id DESC) AS rn
                    FROM PartidasPresupuestarias
                )
                DELETE FROM DuplicadosCodigo WHERE rn > 1;

                ;WITH DuplicadosEtapa AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY ObraId, Etapa ORDER BY CreatedAt DESC, Id DESC) AS rn
                    FROM PartidasPresupuestarias
                )
                DELETE FROM DuplicadosEtapa WHERE rn > 1;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_PartidasPresupuestarias_ObraId_CodigoPartida",
                table: "PartidasPresupuestarias",
                columns: new[] { "ObraId", "CodigoPartida" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartidasPresupuestarias_ObraId_Etapa",
                table: "PartidasPresupuestarias",
                columns: new[] { "ObraId", "Etapa" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PartidasPresupuestarias_ObraId_CodigoPartida",
                table: "PartidasPresupuestarias");

            migrationBuilder.DropIndex(
                name: "IX_PartidasPresupuestarias_ObraId_Etapa",
                table: "PartidasPresupuestarias");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "PartidasPresupuestarias");
        }
    }
}
