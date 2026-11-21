using System;
using System.Collections.Generic;
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
    public class InsumoRepositoryTests
    {
        private static async Task<RequisicionesDbContext> CreateSeededDbContextAsync()
        {
            var options = new DbContextOptionsBuilder<RequisicionesDbContext>()
                .UseInMemoryDatabase(databaseName: $"DB_Req_Insumos_{Guid.NewGuid()}")
                .Options;

            var context = new RequisicionesDbContext(options);

            var insumos = new List<Insumo>
            {
                new(Guid.NewGuid(), "MAT-ARI-001", "Arena Gruesa Lavada de Río", UnidadesMedida.MetroCubico, activo: true, precioReferencia: 28000m),
                new(Guid.NewGuid(), "MAT-ARI-002", "Piedra Partida Granítica 6-20 mm", UnidadesMedida.MetroCubico, activo: true, precioReferencia: 42000m),
                new(Guid.NewGuid(), "MAT-ARI-003", "Cascote Picado Limpio de Ladrillo", UnidadesMedida.MetroCubico, activo: true, precioReferencia: 19500m),
                new(Guid.NewGuid(), "MAT-CAL-001", "Cal Hidratada en Polvo (bolsa 25 kg)", UnidadesMedida.Bolsa, activo: true, precioReferencia: 4800m),
                new(Guid.NewGuid(), "MAT-CEM-001", "Cemento Portland Normal (bolsa 25 kg)", UnidadesMedida.Bolsa, activo: true, precioReferencia: 8500m),
                new(Guid.NewGuid(), "MAT-CEM-002", "Cemento de Albañilería Plasticor (bolsa 25 kg)", UnidadesMedida.Bolsa, activo: true, precioReferencia: 7200m),
                new(Guid.NewGuid(), "MAT-INA-001", "Insumo Descontinuado Inactivo", UnidadesMedida.Unidad, activo: false, precioReferencia: 1000m)
            };

            context.Insumos.AddRange(insumos);
            await context.SaveChangesAsync();

            return context;
        }

        [Fact]
        public async Task GetFilteredAsync_SinFiltroSearch_PorDefecto_DebeRetornarSoloActivosOrdenadosPorCodigo()
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new InsumoRepository(context);

            var resultado = await repository.GetFilteredAsync(
                search: null,
                soloActivos: true,
                cancellationToken: CancellationToken.None);

            Assert.Equal(6, resultado.Total);
            Assert.Equal(6, resultado.Items.Count);
            Assert.All(resultado.Items, i => Assert.True(i.Activo));

            // Orden ascendente por Código
            Assert.Equal("MAT-ARI-001", resultado.Items[0].Codigo);
            Assert.Equal("MAT-ARI-002", resultado.Items[1].Codigo);
            Assert.Equal("MAT-ARI-003", resultado.Items[2].Codigo);
            Assert.Equal("MAT-CAL-001", resultado.Items[3].Codigo);
            Assert.Equal("MAT-CEM-001", resultado.Items[4].Codigo);
            Assert.Equal("MAT-CEM-002", resultado.Items[5].Codigo);
        }

        [Fact]
        public async Task GetFilteredAsync_ConSoloActivosFalso_DebeIncluirInactivos()
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new InsumoRepository(context);

            var resultado = await repository.GetFilteredAsync(
                search: null,
                soloActivos: false,
                cancellationToken: CancellationToken.None);

            Assert.Equal(7, resultado.Total);
            Assert.Equal(7, resultado.Items.Count);
            Assert.Contains(resultado.Items, i => !i.Activo && i.Codigo == "MAT-INA-001");
        }

        [Fact]
        public async Task GetFilteredAsync_ConParametroSearch_BusquedaPorCodigo_DebeRetornarInsumosQueCoincidan()
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new InsumoRepository(context);

            var resultado = await repository.GetFilteredAsync(
                search: "CEM",
                soloActivos: true,
                cancellationToken: CancellationToken.None);

            Assert.Equal(2, resultado.Total);
            Assert.Equal(2, resultado.Items.Count);
            Assert.Contains(resultado.Items, i => i.Codigo == "MAT-CEM-001");
            Assert.Contains(resultado.Items, i => i.Codigo == "MAT-CEM-002");
        }

        [Fact]
        public async Task GetFilteredAsync_ConParametroSearch_BusquedaPorDescripcion_DebeRetornarInsumosQueCoincidan()
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new InsumoRepository(context);

            var resultado = await repository.GetFilteredAsync(
                search: "Portland",
                soloActivos: true,
                cancellationToken: CancellationToken.None);

            Assert.Equal(1, resultado.Total);
            Assert.Single(resultado.Items);
            Assert.Equal("MAT-CEM-001", resultado.Items[0].Codigo);
        }

        [Theory]
        [InlineData("cemento")]
        [InlineData("CEMENTO")]
        [InlineData("Cemento")]
        [InlineData("cEmEnTo")]
        public async Task GetFilteredAsync_InsensibleAMayusculasYMinusculas_DebeDevolverMismosResultados(string terminoBusqueda)
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new InsumoRepository(context);

            var resultado = await repository.GetFilteredAsync(
                search: terminoBusqueda,
                soloActivos: true,
                cancellationToken: CancellationToken.None);

            Assert.Equal(2, resultado.Total);
            Assert.Equal(2, resultado.Items.Count);
            Assert.Equal("MAT-CEM-001", resultado.Items[0].Codigo);
            Assert.Equal("MAT-CEM-002", resultado.Items[1].Codigo);
        }

        [Fact]
        public async Task GetFilteredAsync_ConEspaciosAlrededor_DebeHacerTrimYBuscarCorrectamente()
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new InsumoRepository(context);

            var resultado = await repository.GetFilteredAsync(
                search: "   cemento   ",
                soloActivos: true,
                cancellationToken: CancellationToken.None);

            Assert.Equal(2, resultado.Total);
            Assert.Equal(2, resultado.Items.Count);
            Assert.Equal("MAT-CEM-001", resultado.Items[0].Codigo);
            Assert.Equal("MAT-CEM-002", resultado.Items[1].Codigo);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GetFilteredAsync_ConSearchVacioOEspacios_DebeIgnorarFiltroYDevolverTodosLosActivos(string searchVacio)
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new InsumoRepository(context);

            var resultado = await repository.GetFilteredAsync(
                search: searchVacio,
                soloActivos: true,
                cancellationToken: CancellationToken.None);

            Assert.Equal(6, resultado.Total);
            Assert.Equal(6, resultado.Items.Count);
        }

        [Fact]
        public async Task GetFilteredAsync_ConTerminoInexistente_DebeDevolverListaVacia()
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new InsumoRepository(context);

            var resultado = await repository.GetFilteredAsync(
                search: "TERMINO_INEXISTENTE_XYZ",
                soloActivos: true,
                cancellationToken: CancellationToken.None);

            Assert.NotNull(resultado);
            Assert.Equal(0, resultado.Total);
            Assert.Empty(resultado.Items);
        }

        [Fact]
        public async Task GetFilteredAsync_SensibilidadATildes_DebeDistinguirConYSinTilde()
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new InsumoRepository(context);

            // Búsqueda con tilde "Río" encuentra "Arena Gruesa Lavada de Río"
            var conTildeRio = await repository.GetFilteredAsync(
                search: "Río",
                soloActivos: true,
                cancellationToken: CancellationToken.None);
            Assert.Single(conTildeRio.Items);
            Assert.Equal("MAT-ARI-001", conTildeRio.Items[0].Codigo);

            // Búsqueda sin tilde "rio" no encuentra "Arena Gruesa Lavada de Río" por la collation sensible a acentos (_AS)
            var sinTildeRio = await repository.GetFilteredAsync(
                search: "rio",
                soloActivos: true,
                cancellationToken: CancellationToken.None);
            Assert.Empty(sinTildeRio.Items);

            // Búsqueda con tilde "Granítica" encuentra "Piedra Partida Granítica 6-20 mm"
            var conTildeGranitica = await repository.GetFilteredAsync(
                search: "Granítica",
                soloActivos: true,
                cancellationToken: CancellationToken.None);
            Assert.Single(conTildeGranitica.Items);
            Assert.Equal("MAT-ARI-002", conTildeGranitica.Items[0].Codigo);

            // Búsqueda sin tilde "Granitica" no encuentra nada
            var sinTildeGranitica = await repository.GetFilteredAsync(
                search: "Granitica",
                soloActivos: true,
                cancellationToken: CancellationToken.None);
            Assert.Empty(sinTildeGranitica.Items);

            // Búsqueda con tilde "Albañilería" encuentra "Cemento de Albañilería Plasticor"
            var conTildeAlbanileria = await repository.GetFilteredAsync(
                search: "Albañilería",
                soloActivos: true,
                cancellationToken: CancellationToken.None);
            Assert.Single(conTildeAlbanileria.Items);
            Assert.Equal("MAT-CEM-002", conTildeAlbanileria.Items[0].Codigo);

            // Búsqueda sin tilde "Albanileria" no encuentra nada
            var sinTildeAlbanileria = await repository.GetFilteredAsync(
                search: "Albanileria",
                soloActivos: true,
                cancellationToken: CancellationToken.None);
            Assert.Empty(sinTildeAlbanileria.Items);
        }

        [Fact]
        public async Task GetFilteredAsync_BusquedaSobreInactivos_ConSoloActivosFalso_DebeEncontrarInsumoInactivo()
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new InsumoRepository(context);

            // Con soloActivos = true no debe encontrar el inactivo
            var soloActivosResultado = await repository.GetFilteredAsync(
                search: "Descontinuado",
                soloActivos: true,
                cancellationToken: CancellationToken.None);
            Assert.Empty(soloActivosResultado.Items);

            // Con soloActivos = false sí debe encontrarlo
            var incluyendoInactivosResultado = await repository.GetFilteredAsync(
                search: "Descontinuado",
                soloActivos: false,
                cancellationToken: CancellationToken.None);
            Assert.Single(incluyendoInactivosResultado.Items);
            Assert.Equal("MAT-INA-001", incluyendoInactivosResultado.Items[0].Codigo);
            Assert.False(incluyendoInactivosResultado.Items[0].Activo);
        }

        [Fact]
        public async Task GetFilteredAsync_ConPaginacion_DebeRetornarPaginaSolicitadaYTotalFiltrado()
        {
            await using var context = await CreateSeededDbContextAsync();
            var repository = new InsumoRepository(context);

            // Total activos: 6. Pagina 1, Tamano 2 -> 2 primeros
            var p1 = await repository.GetFilteredAsync(
                search: null,
                soloActivos: true,
                paginacion: new ParametrosPagina(Pagina: 1, Tamano: 2));

            Assert.Equal(6, p1.Total);
            Assert.Equal(2, p1.Items.Count);
            Assert.Equal("MAT-ARI-001", p1.Items[0].Codigo);
            Assert.Equal("MAT-ARI-002", p1.Items[1].Codigo);

            // Pagina 3, Tamano 2 -> 2 últimos
            var p3 = await repository.GetFilteredAsync(
                search: null,
                soloActivos: true,
                paginacion: new ParametrosPagina(Pagina: 3, Tamano: 2));

            Assert.Equal(6, p3.Total);
            Assert.Equal(2, p3.Items.Count);
            Assert.Equal("MAT-CEM-001", p3.Items[0].Codigo);
            Assert.Equal("MAT-CEM-002", p3.Items[1].Codigo);
        }
    }
}
