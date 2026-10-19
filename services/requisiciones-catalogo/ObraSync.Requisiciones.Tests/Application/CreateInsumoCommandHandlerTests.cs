using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Commands;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Handlers;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Domain.Entities;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Application
{
    public class CreateInsumoCommandHandlerTests
    {
        [Fact]
        public async Task Handle_ConDatosValidos_DebeCrearInsumoYGuardarEnRepositorio()
        {
            var repository = Substitute.For<IInsumoRepository>();
            repository.ExistsByCodigoAsync("MAT-CAL-001", Arg.Any<CancellationToken>())
                .Returns(false);

            var handler = new CreateInsumoCommandHandler(repository);

            var command = new CreateInsumoCommand(
                "MAT-CAL-001",
                "Cal Hidratada 25kg",
                UnidadesMedida.Bolsa,
                true,
                4800.00m,
                Monedas.PesoArgentino);

            var resultado = await handler.Handle(command, CancellationToken.None);

            Assert.NotEqual(Guid.Empty, resultado.ArticuloId);
            Assert.Equal("MAT-CAL-001", resultado.Codigo);
            Assert.Equal("Cal Hidratada 25kg", resultado.Descripcion);
            Assert.Equal(UnidadesMedida.Bolsa, resultado.UnidadMedida);
            Assert.Equal(Monedas.PesoArgentino, resultado.Moneda);
            Assert.Equal(4800.00m, resultado.PrecioReferencia);
            Assert.True(resultado.Activo);

            await repository.Received(1).AddAsync(
                Arg.Is<Insumo>(i => i.Codigo == "MAT-CAL-001" && i.Moneda == Monedas.PesoArgentino),
                Arg.Any<CancellationToken>());
            await repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task Handle_ConCodigoDuplicado_DebeLanzarInvalidOperationException()
        {
            var repository = Substitute.For<IInsumoRepository>();
            repository.ExistsByCodigoAsync("MAT-CEM-001", Arg.Any<CancellationToken>())
                .Returns(true);

            var handler = new CreateInsumoCommandHandler(repository);

            var command = new CreateInsumoCommand("MAT-CEM-001", "Otro cemento", UnidadesMedida.Bolsa);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                handler.Handle(command, CancellationToken.None));

            await repository.DidNotReceive().AddAsync(Arg.Any<Insumo>(), Arg.Any<CancellationToken>());
        }
    }
}
