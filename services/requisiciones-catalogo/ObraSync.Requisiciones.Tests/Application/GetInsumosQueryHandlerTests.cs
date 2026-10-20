using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Handlers;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Queries;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Domain.Entities;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Application
{
    public class GetInsumosQueryHandlerTests
    {
        [Fact]
        public async Task Handle_SoloActivosPorDefecto_DebeRetornarSoloActivosMapeadosADto()
        {
            var repository = Substitute.For<IInsumoRepository>();
            var insumosActivos = new List<Insumo>
            {
                new(Guid.NewGuid(), "MAT-ARI-001", "Arena Gruesa Lavada", UnidadesMedida.MetroCubico, true, 28000m),
                new(Guid.NewGuid(), "MAT-CAL-001", "Cal Hidratada 25kg", UnidadesMedida.Bolsa, true, 4800m),
                new(Guid.NewGuid(), "MAT-CEM-001", "Cemento Portland Normal 50kg", UnidadesMedida.Bolsa, true, 8500m)
            };

            repository.GetFilteredAsync(null, true, Arg.Any<CancellationToken>())
                .Returns(insumosActivos);

            var handler = new GetInsumosQueryHandler(repository);

            var query = new GetInsumosQuery(SoloActivos: true);
            var resultado = await handler.Handle(query, CancellationToken.None);

            Assert.Equal(3, resultado.Count);
            Assert.All(resultado, i =>
            {
                Assert.True(i.Activo);
                Assert.Equal(Monedas.PesoArgentino, i.Moneda);
            });
        }

        [Fact]
        public async Task Handle_ConSoloActivosFalso_DebeIncluirInactivos()
        {
            var repository = Substitute.For<IInsumoRepository>();
            var todosLosInsumos = new List<Insumo>
            {
                new(Guid.NewGuid(), "MAT-ARI-001", "Arena Gruesa Lavada", UnidadesMedida.MetroCubico, true),
                new(Guid.NewGuid(), "MAT-ARI-002", "Piedra Partida 6-20", UnidadesMedida.MetroCubico, false)
            };

            repository.GetFilteredAsync(null, false, Arg.Any<CancellationToken>())
                .Returns(todosLosInsumos);

            var handler = new GetInsumosQueryHandler(repository);

            var query = new GetInsumosQuery(SoloActivos: false);
            var resultado = await handler.Handle(query, CancellationToken.None);

            Assert.Equal(2, resultado.Count);
        }

        [Fact]
        public async Task Handle_ConFiltroBusqueda_DebeDelegarFiltroAlRepositorio()
        {
            var repository = Substitute.For<IInsumoRepository>();
            var filtrados = new List<Insumo>
            {
                new(Guid.NewGuid(), "MAT-ARI-001", "Arena Gruesa Lavada", UnidadesMedida.MetroCubico, true, 28000m)
            };

            repository.GetFilteredAsync("Arena", true, Arg.Any<CancellationToken>())
                .Returns(filtrados);

            var handler = new GetInsumosQueryHandler(repository);

            var query = new GetInsumosQuery(Search: "Arena", SoloActivos: true);
            var resultado = await handler.Handle(query, CancellationToken.None);

            Assert.Single(resultado);
            Assert.Equal("MAT-ARI-001", resultado[0].Codigo);
            Assert.Equal("ARS", resultado[0].Moneda);
            await repository.Received(1).GetFilteredAsync("Arena", true, Arg.Any<CancellationToken>());
        }
    }
}
