using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Application.Services;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Domain.Entities;
using ObraSync.Requisiciones.Infrastructure.Persistence;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Application
{
    public class CatalogoServiceTests
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
        public async Task ListarInsumos_SoloActivosPorDefecto_DebeRetornarSoloActivos()
        {
            using var context = await CreateInMemoryDbContextAsync();
            var service = new CatalogoService(context);

            var resultado = await service.ListarInsumosAsync(soloActivos: true);

            Assert.Equal(3, resultado.Count);
            Assert.All(resultado, i => Assert.True(i.Activo));
        }

        [Fact]
        public async Task ListarInsumos_ConSoloActivosFalso_DebeIncluirInactivos()
        {
            using var context = await CreateInMemoryDbContextAsync();
            var service = new CatalogoService(context);

            var resultado = await service.ListarInsumosAsync(soloActivos: false);

            Assert.Equal(4, resultado.Count);
        }

        [Fact]
        public async Task ListarInsumos_ConFiltroBusqueda_DebeFiltrarPorCodigoODescripcion()
        {
            using var context = await CreateInMemoryDbContextAsync();
            var service = new CatalogoService(context);

            var resultado = await service.ListarInsumosAsync(search: "Arena");

            Assert.Single(resultado);
            Assert.Equal("MAT-ARI-001", resultado[0].Codigo);
        }
    }
}
