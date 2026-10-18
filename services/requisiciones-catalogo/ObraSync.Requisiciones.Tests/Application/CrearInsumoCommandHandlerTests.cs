using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Application.Commands.CrearInsumo;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Domain.Entities;
using ObraSync.Requisiciones.Infrastructure.Persistence;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Application
{
    public class CrearInsumoCommandHandlerTests
    {
        private async Task<RequisicionesDbContext> CreateInMemoryDbContextAsync()
        {
            var options = new DbContextOptionsBuilder<RequisicionesDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var context = new RequisicionesDbContext(options);

            context.Insumos.Add(
                new Insumo(Guid.NewGuid(), "MAT-CEM-001", "Cemento Existente", UnidadesMedida.Bolsa, true)
            );

            await context.SaveChangesAsync();
            return context;
        }

        [Fact]
        public async Task Handle_ConDatosValidos_DebeCrearInsumoYGuardarEnBase()
        {
            using var context = await CreateInMemoryDbContextAsync();
            var handler = new CrearInsumoCommandHandler(context);

            var command = new CrearInsumoCommand("MAT-CAL-001", "Cal Hidratada 25kg", UnidadesMedida.Bolsa);
            var resultado = await handler.Handle(command, CancellationToken.None);

            Assert.NotEqual(Guid.Empty, resultado.ArticuloId);
            Assert.Equal("MAT-CAL-001", resultado.Codigo);
            Assert.Equal("Cal Hidratada 25kg", resultado.Descripcion);
            Assert.Equal(UnidadesMedida.Bolsa, resultado.UnidadMedida);
            Assert.True(resultado.Activo);

            var guardado = await context.Insumos.FirstOrDefaultAsync(i => i.Codigo == "MAT-CAL-001");
            Assert.NotNull(guardado);
        }

        [Fact]
        public async Task Handle_ConCodigoDuplicado_DebeLanzarInvalidOperationException()
        {
            using var context = await CreateInMemoryDbContextAsync();
            var handler = new CrearInsumoCommandHandler(context);

            var command = new CrearInsumoCommand("MAT-CEM-001", "Otro cemento", UnidadesMedida.Bolsa);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                handler.Handle(command, CancellationToken.None));
        }
    }
}
