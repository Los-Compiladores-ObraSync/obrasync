using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Adjudicar.Commands;
using ObraSync.Licitaciones.Application.UseCases.Adjudicar.Handlers;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure.Messaging;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class AdjudicarCotizacionConcurrenciaTests
{
    [Fact]
    public async Task Handle_Debe_Lanzar_InvalidOperationException_Cuando_Ocurre_DbUpdateConcurrencyException()
    {
        // Arrange
        var licitacionRepository = Substitute.For<ILicitacionRepository>();
        var cotizacionRepository = Substitute.For<ICotizacionRepository>();
        var ordenCompraRepository = Substitute.For<IOrdenCompraRepository>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<AdjudicarCotizacionCommandHandler>>();

        var licitacionId = Guid.NewGuid();
        var cotizacionId = Guid.NewGuid();

        var licitacion = new Licitacion
        {
            LicitacionId = licitacionId,
            Estado = EstadoLicitacion.Abierta,
            Items = new List<LicitacionItem>()
        };

        var cotizacion = new Cotizacion
        {
            CotizacionId = cotizacionId,
            LicitacionId = licitacionId,
            Items = new List<CotizacionItem>()
        };

        licitacionRepository
            .GetByIdAsync(licitacionId, Arg.Any<CancellationToken>())
            .Returns(licitacion);

        cotizacionRepository
            .GetByIdAsync(cotizacionId, Arg.Any<CancellationToken>())
            .Returns(cotizacion);

        // Simulamos que al guardar los cambios salta el conflicto de concurrencia optimista
        unitOfWork
            .SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("La licitación fue modificada o adjudicada por otro usuario en simultáneo. Por favor, actualice los datos e intente nuevamente."));

        var handler = new AdjudicarCotizacionCommandHandler(
            licitacionRepository,
            cotizacionRepository,
            ordenCompraRepository,
            new MassTransitLicitacionEventPublisher(publishEndpoint),
            unitOfWork,
            logger
        );

        var command = new AdjudicarCotizacionCommand(licitacionId, cotizacionId, Guid.NewGuid());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(command, CancellationToken.None));

        // Verificamos que el mensaje sea el amigable que configuramos para evitar el error 500
        Assert.Contains("licitación fue modificada o adjudicada por otro usuario", exception.Message);
    }
}
