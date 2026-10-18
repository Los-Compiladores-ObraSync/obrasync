using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Application.Queries.ListarInsumos;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Domain.Entities;
using ObraSync.Requisiciones.Infrastructure.Persistence;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Application
{
    public class ListarInsumosQueryHandlerTests
    {
        private async Task<RequisicionesDbContext> CreateInMemoryDbContextAsync()
        {
            var options = new DbContextOptionsBuilder<RequisicionesDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var context = new RequisicionesDbContext(options);

            context.Insumos.AddRange(
                new Insumo(Guid.NewGuid(), "MAT-CEM-001", "Cemento Portland Normal 50kg", UnidadesMedida.Bolsa, true),
                new Insumo(Guid.NewGuid(), "MAT-CAL-001", "Cal Hidratada 25kg", UnidadesMedida.Bolsa, true),
                new Insumo(Guid.NewGuid(), "MAT-ARI-001", "Arena Gruesa Lavada", UnidadesMedida.MetroCubico, true),
                new Insumo(Guid.NewGuid(), "MAT-ARI-002", "Piedra Partida 6-20", UnidadesMedida.MetroCubico, false) // Inactivo
            );

            await context.SaveChangesAsync();
            return context;
        }

        [Fact]
        public async Task Handle_SoloActivosPorDefecto_DebeRetornarSoloActivos()
        {
            using var context = await CreateInMemoryDbContextAsync();
            var handler = new ListarInsumosQueryHandler(context);

            var query = new ListarInsumosQuery(SoloActivos: true);
            var resultado = await handler.Handle(query, CancellationToken.None);

            Assert.Equal(3, resultado.Count);
            Assert.All(resultado, i => Assert.True(i.Activo));
        }

        [Fact]
        public async Task Handle_ConSoloActivosFalso_DebeIncluirInactivos()
        {
            using var context = await CreateInMemoryDbContextAsync();
            var handler = new ListarInsumosQueryHandler(context);

            var query = new ListarInsumosQuery(SoloActivos: false);
            var resultado = await handler.Handle(query, CancellationToken.None);

            Assert.Equal(4, resultado.Count);
        }

        [Fact]
        public async Task Handle_ConFiltroBusqueda_DebeFiltrarPorCodigoODescripcion()
        {
            using var context = await CreateInMemoryDbContextAsync();
            var handler = new ListarInsumosQueryHandler(context);

            var query = new ListarInsumosQuery(Search: "Arena");
            var resultado = await handler.Handle(query, CancellationToken.None);

            Assert.Single(resultado);
            Assert.Equal("MAT-ARI-001", resultado[0].Codigo);
        }
    }
}
