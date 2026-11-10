using MassTransit;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Adjudicar.Commands;
using ObraSync.Licitaciones.Application.UseCases.Adjudicar.Handlers;
using ObraSync.Licitaciones.Domain.Entities;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class AdjudicarOutboxTests
{
    private readonly ILicitacionRepository _licitaciones = Substitute.For<ILicitacionRepository>();
    private readonly ICotizacionRepository _cotizaciones = Substitute.For<ICotizacionRepository>();
    private readonly IOrdenCompraRepository _ordenes = Substitute.For<IOrdenCompraRepository>();
    private readonly IPublishEndpoint _publish = Substitute.For<IPublishEndpoint>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private (AdjudicarCotizacionCommandHandler handler, AdjudicarCotizacionCommand comando, Licitacion licitacion) Preparar()
    {
        var articuloId = Guid.NewGuid();
        var licitacionId = Guid.NewGuid();
        var cotizacionId = Guid.NewGuid();

        var licitacion = new Licitacion
        {
            LicitacionId = licitacionId,
            RequisicionId = Guid.NewGuid(),
            ObraId = Guid.NewGuid(),
            Estado = EstadoLicitacion.Abierta,
            Items = new List<LicitacionItem>
            {
                new() { Id = Guid.NewGuid(), LicitacionId = licitacionId, ArticuloId = articuloId,
                        Descripcion = "Cemento", Cantidad = 80m, UnidadMedida = "BOLSA" }
            }
        };

        var cotizacion = new Cotizacion
        {
            CotizacionId = cotizacionId,
            LicitacionId = licitacionId,
            ProveedorId = Guid.NewGuid(),
            Flete = 1500m,
            PlazoEntregaDias = 5,
            Items = new List<CotizacionItem>
            {
                new() { Id = Guid.NewGuid(), CotizacionId = cotizacionId, ArticuloId = articuloId, PrecioUnitario = 100m }
            }
        };

        _licitaciones.GetByIdAsync(licitacionId, Arg.Any<CancellationToken>()).Returns(licitacion);
        _cotizaciones.GetByIdAsync(cotizacionId, Arg.Any<CancellationToken>()).Returns(cotizacion);

        var handler = new AdjudicarCotizacionCommandHandler(
            _licitaciones, _cotizaciones, _ordenes, _publish, _unitOfWork);

        return (handler, new AdjudicarCotizacionCommand(licitacionId, cotizacionId), licitacion);
    }

    [Fact]
    public async Task Publica_el_evento_ANTES_de_guardar_y_guarda_una_sola_vez()
    {
        var (handler, comando, _) = Preparar();

        await handler.Handle(comando, CancellationToken.None);

        // Orden: primero Publish, después SaveChanges (con el outbox, ambos van en la misma transacción)
        Received.InOrder(() =>
        {
            _publish.Publish<OrdenCompraEmitidaEvent>(Arg.Any<OrdenCompraEmitidaEvent>(), Arg.Any<CancellationToken>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deja_la_licitacion_Adjudicada_y_el_evento_lleva_el_monto_total()
    {
        var (handler, comando, licitacion) = Preparar();

        await handler.Handle(comando, CancellationToken.None);

        Assert.Equal(EstadoLicitacion.Adjudicada, licitacion.Estado);
        await _publish.Received(1).Publish<OrdenCompraEmitidaEvent>(
            Arg.Is<OrdenCompraEmitidaEvent>(e => e.MontoTotal == 9500m), // 100 x 80 + flete 1500
            Arg.Any<CancellationToken>());
    }
}