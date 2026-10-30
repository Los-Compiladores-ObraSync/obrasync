using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Domain.Entities;
using ObraSync.Requisiciones.Infrastructure.Persistence;
using ObraSync.Requisiciones.Infrastructure.Persistence.Repositories;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Infrastructure
{
    public class RequisicionRepositoryTests
    {
        private static readonly Guid ObraAlphaId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaa0001");
        private static readonly Guid ObraBetaId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb0002");

        private static async Task<RequisicionesDbContext> CreateSeededDbContextAsync()
        {
            var options = new DbContextOptionsBuilder<RequisicionesDbContext>()
                .UseInMemoryDatabase(databaseName: $"DB_Req_Repo_{Guid.NewGuid()}")
                .Options;

            var context = new RequisicionesDbContext(options);

            var solicitanteId = Guid.NewGuid();
            var aprobadorId = Guid.NewGuid();
            var cementoId = Guid.Parse("11111111-1111-1111-1111-111111110001");
            var calId = Guid.Parse("11111111-1111-1111-1111-111111110003");
            var hierroId = Guid.Parse("11111111-1111-1111-1111-111111110007");

            // Requisición 1: Más antigua, estado Pendiente, Obra Alpha
            var req1 = new Requisicion(
                Guid.NewGuid(),
                "REQ-2026-0001",
                ObraAlphaId,
                solicitanteId,
                new[]
                {
                    new RequisicionItem(Guid.NewGuid(), Guid.Empty, cementoId, "Cemento Portland Normal (bolsa 50 kg)", 80m, UnidadesMedida.Bolsa)
                },
                etapa: "Fundaciones",
                fechaCreacion: new DateTime(2026, 10, 18, 8, 0, 0, DateTimeKind.Utc));

            // Requisición 2: Fecha intermedia, estado Aprobada, Obra Alpha
            var req2 = new Requisicion(
                Guid.NewGuid(),
                "REQ-2026-0002",
                ObraAlphaId,
                solicitanteId,
                new[]
                {
                    new RequisicionItem(Guid.NewGuid(), Guid.Empty, calId, "Cal Hidratada en Polvo (bolsa 25 kg)", 40m, UnidadesMedida.Bolsa)
                },
                etapa: "Mampostería",
                fechaCreacion: new DateTime(2026, 10, 19, 10, 30, 0, DateTimeKind.Utc));
            req2.Aprobar(aprobadorId, new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc));

            // Requisición 3: Más reciente, estado Pendiente, Obra Beta
            var req3 = new Requisicion(
                Guid.NewGuid(),
                "REQ-2026-0003",
                ObraBetaId,
                solicitanteId,
                new[]
                {
                    new RequisicionItem(Guid.NewGuid(), Guid.Empty, hierroId, "Hierro Conformado ADN 420 Ø 8 mm (barra 12 m)", 120m, UnidadesMedida.Unidad)
                },
                etapa: "Estructura",
                fechaCreacion: new DateTime(2026, 10, 20, 15, 45, 0, DateTimeKind.Utc));

            context.Requisiciones.AddRange(req1, req2, req3);
            await context.SaveChangesAsync();

            return context;
        }

        [Fact]
        public async Task GetFilteredAsync_SinFiltros_DebeDevolverTodosLosEstadosOrdenadosPorFechaDescendenteYConItems()
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new RequisicionRepository(context);

            var resultado = await repository.GetFilteredAsync(
                obraId: null,
                estado: null,
                cancellationToken: CancellationToken.None);

            Assert.Equal(3, resultado.Count);

            // Orden descendente por FechaCreacion: REQ-2026-0003 (20/10) -> REQ-2026-0002 (19/10) -> REQ-2026-0001 (18/10)
            Assert.Equal("REQ-2026-0003", resultado[0].NumeroRequisicion);
            Assert.Equal(EstadoRequisicion.Pendiente, resultado[0].Estado);
            Assert.Single(resultado[0].Items);

            Assert.Equal("REQ-2026-0002", resultado[1].NumeroRequisicion);
            Assert.Equal(EstadoRequisicion.Aprobada, resultado[1].Estado);
            Assert.Single(resultado[1].Items);

            Assert.Equal("REQ-2026-0001", resultado[2].NumeroRequisicion);
            Assert.Equal(EstadoRequisicion.Pendiente, resultado[2].Estado);
            Assert.Single(resultado[2].Items);

            Assert.True(resultado[0].FechaCreacion > resultado[1].FechaCreacion);
            Assert.True(resultado[1].FechaCreacion > resultado[2].FechaCreacion);
        }

        [Fact]
        public async Task GetFilteredAsync_ConFiltroObraId_DebeDevolverSoloRequisicionesDeEsaObra()
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new RequisicionRepository(context);

            var resultadoObraAlpha = await repository.GetFilteredAsync(
                obraId: ObraAlphaId,
                estado: null,
                cancellationToken: CancellationToken.None);

            Assert.Equal(2, resultadoObraAlpha.Count);
            Assert.All(resultadoObraAlpha, r => Assert.Equal(ObraAlphaId, r.ObraId));
            Assert.Equal("REQ-2026-0002", resultadoObraAlpha[0].NumeroRequisicion);
            Assert.Equal("REQ-2026-0001", resultadoObraAlpha[1].NumeroRequisicion);
        }

        [Theory]
        [InlineData("APROBADA", 1, EstadoRequisicion.Aprobada)]
        [InlineData("aprobada", 1, EstadoRequisicion.Aprobada)]
        [InlineData("PENDIENTE", 2, EstadoRequisicion.Pendiente)]
        [InlineData("pendiente", 2, EstadoRequisicion.Pendiente)]
        public async Task GetFilteredAsync_ConFiltroEstadoEnMayusculasYMinusculas_DebeIgnorarCaseSensitivity(
            string filtroEstado,
            int cantidadEsperada,
            string estadoEsperado)
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new RequisicionRepository(context);

            var resultado = await repository.GetFilteredAsync(
                obraId: null,
                estado: filtroEstado,
                cancellationToken: CancellationToken.None);

            Assert.Equal(cantidadEsperada, resultado.Count);
            Assert.All(resultado, r => Assert.Equal(estadoEsperado, r.Estado));
        }

        [Fact]
        public async Task GetFilteredAsync_ConEstadoInexistente_DebeDevolverListaVaciaSinExcepcion()
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new RequisicionRepository(context);

            var resultado = await repository.GetFilteredAsync(
                obraId: null,
                estado: "INEXISTENTE",
                cancellationToken: CancellationToken.None);

            Assert.NotNull(resultado);
            Assert.Empty(resultado);
        }
    }
}
