using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Application.DTOs;
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

            Assert.Equal(3, resultado.Total);
            Assert.Equal(3, resultado.Items.Count);

            // Orden descendente por FechaCreacion: REQ-2026-0003 (20/10) -> REQ-2026-0002 (19/10) -> REQ-2026-0001 (18/10)
            Assert.Equal("REQ-2026-0003", resultado.Items[0].NumeroRequisicion);
            Assert.Equal(EstadoRequisicion.Pendiente, resultado.Items[0].Estado);
            Assert.Single(resultado.Items[0].Items);

            Assert.Equal("REQ-2026-0002", resultado.Items[1].NumeroRequisicion);
            Assert.Equal(EstadoRequisicion.Aprobada, resultado.Items[1].Estado);
            Assert.Single(resultado.Items[1].Items);

            Assert.Equal("REQ-2026-0001", resultado.Items[2].NumeroRequisicion);
            Assert.Equal(EstadoRequisicion.Pendiente, resultado.Items[2].Estado);
            Assert.Single(resultado.Items[2].Items);

            Assert.True(resultado.Items[0].FechaCreacion > resultado.Items[1].FechaCreacion);
            Assert.True(resultado.Items[1].FechaCreacion > resultado.Items[2].FechaCreacion);
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

            Assert.Equal(2, resultadoObraAlpha.Total);
            Assert.Equal(2, resultadoObraAlpha.Items.Count);
            Assert.All(resultadoObraAlpha.Items, r => Assert.Equal(ObraAlphaId, r.ObraId));
            Assert.Equal("REQ-2026-0002", resultadoObraAlpha.Items[0].NumeroRequisicion);
            Assert.Equal("REQ-2026-0001", resultadoObraAlpha.Items[1].NumeroRequisicion);
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

            Assert.Equal(cantidadEsperada, resultado.Total);
            Assert.Equal(cantidadEsperada, resultado.Items.Count);
            Assert.All(resultado.Items, r => Assert.Equal(estadoEsperado, r.Estado));
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
            Assert.Equal(0, resultado.Total);
            Assert.Empty(resultado.Items);
        }

        [Fact]
        public async Task GetByIdempotencyKeyAsync_DebeEncontrarRequisicionDelMismoSolicitanteYConSusItems()
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new RequisicionRepository(context);
            var solicitanteId = Guid.NewGuid();
            var cementoId = Guid.Parse("11111111-1111-1111-1111-111111110001");

            var req = new Requisicion(
                Guid.NewGuid(),
                "REQ-2026-0099",
                ObraAlphaId,
                solicitanteId,
                new[]
                {
                    new RequisicionItem(Guid.NewGuid(), Guid.Empty, cementoId, "Cemento Portland", 25m, UnidadesMedida.Bolsa)
                },
                etapa: "Estructura",
                idempotencyKey: "clave-unica-123");

            await repository.AddAsync(req);
            await repository.SaveChangesAsync();

            var encontrada = await repository.GetByIdempotencyKeyAsync(solicitanteId, "clave-unica-123");
            Assert.NotNull(encontrada);
            Assert.Equal("REQ-2026-0099", encontrada.NumeroRequisicion);
            Assert.Single(encontrada.Items);

            var otroSolicitante = await repository.GetByIdempotencyKeyAsync(Guid.NewGuid(), "clave-unica-123");
            Assert.Null(otroSolicitante);
        }

        [Fact]
        public async Task GetFilteredAsync_ConFiltroSolicitanteId_DebeDevolverSoloRequisicionesDeEseSolicitante()
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new RequisicionRepository(context);

            var solicitanteA = Guid.NewGuid();
            var solicitanteB = Guid.NewGuid();
            var cementoId = Guid.Parse("11111111-1111-1111-1111-111111110001");

            var reqA1 = new Requisicion(
                Guid.NewGuid(),
                "REQ-2026-0050",
                ObraAlphaId,
                solicitanteA,
                new[]
                {
                    new RequisicionItem(Guid.NewGuid(), Guid.Empty, cementoId, "Cemento Portland", 20m, UnidadesMedida.Bolsa)
                },
                etapa: "Etapa A1");

            var reqA2 = new Requisicion(
                Guid.NewGuid(),
                "REQ-2026-0051",
                ObraBetaId,
                solicitanteA,
                new[]
                {
                    new RequisicionItem(Guid.NewGuid(), Guid.Empty, cementoId, "Cemento Portland", 30m, UnidadesMedida.Bolsa)
                },
                etapa: "Etapa A2");

            var reqB1 = new Requisicion(
                Guid.NewGuid(),
                "REQ-2026-0052",
                ObraAlphaId,
                solicitanteB,
                new[]
                {
                    new RequisicionItem(Guid.NewGuid(), Guid.Empty, cementoId, "Cemento Portland", 40m, UnidadesMedida.Bolsa)
                },
                etapa: "Etapa B1");

            await repository.AddAsync(reqA1);
            await repository.AddAsync(reqA2);
            await repository.AddAsync(reqB1);
            await repository.SaveChangesAsync();

            var resultadoA = await repository.GetFilteredAsync(solicitanteId: solicitanteA);
            Assert.Equal(2, resultadoA.Total);
            Assert.Equal(2, resultadoA.Items.Count);
            Assert.All(resultadoA.Items, r => Assert.Equal(solicitanteA, r.SolicitanteId));

            var resultadoB = await repository.GetFilteredAsync(solicitanteId: solicitanteB);
            Assert.Equal(1, resultadoB.Total);
            Assert.Single(resultadoB.Items);
            Assert.Equal(solicitanteB, resultadoB.Items[0].SolicitanteId);
            Assert.Equal("REQ-2026-0052", resultadoB.Items[0].NumeroRequisicion);

            var resultadoInexistente = await repository.GetFilteredAsync(solicitanteId: Guid.NewGuid());
            Assert.Equal(0, resultadoInexistente.Total);
            Assert.Empty(resultadoInexistente.Items);
        }

        [Fact]
        public async Task GetFilteredAsync_ConMismaFechaCreacion_DesempataPorIdSinDuplicarNiOmitirEntrePaginas()
        {
            var options = new DbContextOptionsBuilder<RequisicionesDbContext>()
                .UseInMemoryDatabase(databaseName: $"DB_Req_TieBreak_{Guid.NewGuid()}")
                .Options;

            await using var context = new RequisicionesDbContext(options);
            var repository = new RequisicionRepository(context);

            var fechaIdentica = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
            var solicitanteId = Guid.NewGuid();
            var cementoId = Guid.Parse("11111111-1111-1111-1111-111111110001");

            var ids = new[]
            {
                Guid.Parse("00000000-0000-0000-0000-000000000004"),
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Guid.Parse("00000000-0000-0000-0000-000000000003"),
                Guid.Parse("00000000-0000-0000-0000-000000000002")
            };

            for (int i = 0; i < ids.Length; i++)
            {
                var req = new Requisicion(
                    ids[i],
                    $"REQ-2026-010{i + 1}",
                    ObraAlphaId,
                    solicitanteId,
                    new[]
                    {
                        new RequisicionItem(Guid.NewGuid(), ids[i], cementoId, "Cemento Portland", 10m, UnidadesMedida.Bolsa)
                    },
                    etapa: "Estructura",
                    fechaCreacion: fechaIdentica);
                await repository.AddAsync(req);
            }
            await repository.SaveChangesAsync();

            var p1 = await repository.GetFilteredAsync(paginacion: new ParametrosPagina(1, 2));
            var p2 = await repository.GetFilteredAsync(paginacion: new ParametrosPagina(2, 2));

            Assert.Equal(4, p1.Total);
            Assert.Equal(2, p1.Items.Count);
            Assert.Equal(4, p2.Total);
            Assert.Equal(2, p2.Items.Count);

            var idsObtenidos = p1.Items.Concat(p2.Items).Select(r => r.Id).ToList();
            var idsEsperados = ids.OrderBy(id => id).ToList();

            Assert.Equal(idsEsperados, idsObtenidos);
        }
    }
}
