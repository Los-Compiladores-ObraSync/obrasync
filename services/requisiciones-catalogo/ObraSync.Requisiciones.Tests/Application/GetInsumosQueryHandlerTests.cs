using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using ObraSync.Requisiciones.Application.DTOs;
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

            repository.GetFilteredAsync(null, true, null, Arg.Any<CancellationToken>())
                .Returns(new PaginaDe<Insumo>(insumosActivos, insumosActivos.Count));

            var handler = new GetInsumosQueryHandler(repository);

            var query = new GetInsumosQuery(SoloActivos: true);
            var resultado = await handler.Handle(query, CancellationToken.None);

            Assert.Equal(3, resultado.Items.Count);
            Assert.Equal(3, resultado.Total);
            Assert.All(resultado.Items, i =>
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

            repository.GetFilteredAsync(null, false, null, Arg.Any<CancellationToken>())
                .Returns(new PaginaDe<Insumo>(todosLosInsumos, todosLosInsumos.Count));

            var handler = new GetInsumosQueryHandler(repository);

            var query = new GetInsumosQuery(SoloActivos: false);
            var resultado = await handler.Handle(query, CancellationToken.None);

            Assert.Equal(2, resultado.Items.Count);
            Assert.Equal(2, resultado.Total);
        }

        [Fact]
        public async Task Handle_ConFiltroBusqueda_DebeDelegarFiltroAlRepositorio()
        {
            var repository = Substitute.For<IInsumoRepository>();
            var filtrados = new List<Insumo>
            {
                new(Guid.NewGuid(), "MAT-ARI-001", "Arena Gruesa Lavada", UnidadesMedida.MetroCubico, true, 28000m)
            };

            repository.GetFilteredAsync("Arena", true, null, Arg.Any<CancellationToken>())
                .Returns(new PaginaDe<Insumo>(filtrados, 1));

            var handler = new GetInsumosQueryHandler(repository);

            var query = new GetInsumosQuery(Search: "Arena", SoloActivos: true);
            var resultado = await handler.Handle(query, CancellationToken.None);

            Assert.Single(resultado.Items);
            Assert.Equal(1, resultado.Total);
            Assert.Equal("MAT-ARI-001", resultado.Items[0].Codigo);
            Assert.Equal("ARS", resultado.Items[0].Moneda);
            await repository.Received(1).GetFilteredAsync("Arena", true, null, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task Handle_ConTerminoSinResultados_DebeRetornarListaVacia()
        {
            var repository = Substitute.For<IInsumoRepository>();
            repository.GetFilteredAsync("Inexistente", true, null, Arg.Any<CancellationToken>())
                .Returns(new PaginaDe<Insumo>(new List<Insumo>(), 0));

            var handler = new GetInsumosQueryHandler(repository);

            var query = new GetInsumosQuery(Search: "Inexistente", SoloActivos: true);
            var resultado = await handler.Handle(query, CancellationToken.None);

            Assert.Empty(resultado.Items);
            Assert.Equal(0, resultado.Total);
            await repository.Received(1).GetFilteredAsync("Inexistente", true, null, Arg.Any<CancellationToken>());
        }
    }
}
