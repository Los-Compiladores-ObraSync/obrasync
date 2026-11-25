using Xunit;
using Moq;
using Microsoft.EntityFrameworkCore;
using ObraSync.Licitaciones.Application.UseCases.Adjudicar.Commands;
using ObraSync.Licitaciones.Application.UseCases.Adjudicar.Handlers;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure.Messaging;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace ObraSync.Licitaciones.Tests;

public class AdjudicarCotizacionConcurrenciaTests
{
    [Fact]
    public async Task Handle_Debe_Lanzar_InvalidOperationException_Cuando_Ocurre_DbUpdateConcurrencyException()
    {
        // Arrange
        var licitacionRepositoryMock = new Mock<ILicitacionRepository>();
        var cotizacionRepositoryMock = new Mock<ICotizacionRepository>();
        var ordenCompraRepositoryMock = new Mock<IOrdenCompraRepository>();
        var publishEndpointMock = new Mock<IPublishEndpoint>();
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var loggerMock = new Mock<ILogger<AdjudicarCotizacionCommandHandler>>();

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

        licitacionRepositoryMock
            .Setup(r => r.GetByIdAsync(licitacionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(licitacion);

        cotizacionRepositoryMock
            .Setup(r => r.GetByIdAsync(cotizacionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cotizacion);

        // Simulamos que al guardar los cambios salta el conflicto de concurrencia optimista
        unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("La licitación fue modificada o adjudicada por otro usuario en simultáneo. Por favor, actualice los datos e intente nuevamente."));

        var handler = new AdjudicarCotizacionCommandHandler(
            licitacionRepositoryMock.Object,
            cotizacionRepositoryMock.Object,
            ordenCompraRepositoryMock.Object,
            new MassTransitLicitacionEventPublisher(publishEndpointMock.Object),
            unitOfWorkMock.Object,
            loggerMock.Object
        );

        var command = new AdjudicarCotizacionCommand(licitacionId, cotizacionId, Guid.NewGuid());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(command, CancellationToken.None));

        // Verificamos que el mensaje sea el amigable que configuramos para evitar el error 500
        Assert.Contains("licitación fue modificada o adjudicada por otro usuario", exception.Message);
    }
}