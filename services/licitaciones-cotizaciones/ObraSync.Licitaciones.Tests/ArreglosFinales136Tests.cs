using MassTransit;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Adjudicar.Commands;
using ObraSync.Licitaciones.Application.UseCases.Adjudicar.Handlers;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Handlers;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Queries;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

/// <summary>Pruebas de los arreglos finales #136 en Licitaciones (puntos 1, 4, 7 y 8).</summary>
public class ArreglosFinales136Tests
{
    private readonly ILicitacionRepository _licitaciones = Substitute.For<ILicitacionRepository>();
    private readonly ICotizacionRepository _cotizaciones = Substitute.For<ICotizacionRepository>();
    private readonly IOrdenCompraRepository _ordenes = Substitute.For<IOrdenCompraRepository>();
    private readonly IPublishEndpoint _publish = Substitute.For<IPublishEndpoint>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private (AdjudicarCotizacionCommandHandler handler, AdjudicarCotizacionCommand comando) PrepararAdjudicacion(
        Guid articuloCotizado, Guid articuloLicitado, string? etapaRequisicion)
    {
        var licitacionId = Guid.NewGuid();
        var cotizacionId = Guid.NewGuid();
        var requisicionId = Guid.NewGuid();

        var licitacion = new Licitacion
        {
            LicitacionId = licitacionId,
            RequisicionId = requisicionId,
            ObraId = Guid.NewGuid(),
            Estado = EstadoLicitacion.Abierta,
            Items = new List<LicitacionItem>
            {
                new() { Id = Guid.NewGuid(), LicitacionId = licitacionId, ArticuloId = articuloLicitado,
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
                new() { Id = Guid.NewGuid(), CotizacionId = cotizacionId, ArticuloId = articuloCotizado, PrecioUnitario = 100m }
            }
        };

        _licitaciones.GetByIdAsync(licitacionId, Arg.Any<CancellationToken>()).Returns(licitacion);
        _licitaciones.ObtenerRequisicionAsync(requisicionId, Arg.Any<CancellationToken>())
            .Returns(new RequisicionAprobada { RequisicionId = requisicionId, Etapa = etapaRequisicion });
        _cotizaciones.GetByIdAsync(cotizacionId, Arg.Any<CancellationToken>()).Returns(cotizacion);

        var handler = new AdjudicarCotizacionCommandHandler(
            _licitaciones, _cotizaciones, _ordenes, _publish, _unitOfWork);

        return (handler, new AdjudicarCotizacionCommand(licitacionId, cotizacionId));
    }

    [Fact]
    public async Task Adjudicar_PublicaLaEtapaRealDeLaRequisicion_NoElTextoFijo()
    {
        var articulo = Guid.NewGuid();
        var (handler, comando) = PrepararAdjudicacion(articulo, articulo, "Estructura");

        await handler.Handle(comando, CancellationToken.None);

        await _publish.Received(1).Publish<OrdenCompraEmitidaEvent>(
            Arg.Is<OrdenCompraEmitidaEvent>(e => e.Etapa == "Estructura"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Adjudicar_SiLaRequisicionNoTieneEtapa_PublicaEtapaNula()
    {
        var articulo = Guid.NewGuid();
        var (handler, comando) = PrepararAdjudicacion(articulo, articulo, null);

        await handler.Handle(comando, CancellationToken.None);

        await _publish.Received(1).Publish<OrdenCompraEmitidaEvent>(
            Arg.Is<OrdenCompraEmitidaEvent>(e => e.Etapa == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Adjudicar_ConArticuloQueLaLicitacionNoPidio_LanzaArgumentExceptionYNoGuardaNiPublica()
    {
        var (handler, comando) = PrepararAdjudicacion(Guid.NewGuid(), Guid.NewGuid(), "Estructura");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(comando, CancellationToken.None));

        Assert.Contains("no pertenece a la licitación", ex.Message);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _publish.DidNotReceive().Publish<OrdenCompraEmitidaEvent>(
            Arg.Any<OrdenCompraEmitidaEvent>(), Arg.Any<CancellationToken>());
    }

    private GetLicitacionByIdQueryHandler PrepararConsulta(Guid id, EstadoLicitacion estado)
    {
        _licitaciones.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new Licitacion
        {
            LicitacionId = id,
            RequisicionId = Guid.NewGuid(),
            ObraId = Guid.NewGuid(),
            Estado = estado,
            Items = new List<LicitacionItem>()
        });
        return new GetLicitacionByIdQueryHandler(_licitaciones);
    }

    [Theory]
    [InlineData(EstadoLicitacion.Adjudicada)]
    [InlineData(EstadoLicitacion.Cerrada)]
    public async Task ObtenerPorId_ProveedorConLicitacionNoAbierta_LanzaKeyNotFound(EstadoLicitacion estado)
    {
        var id = Guid.NewGuid();
        var handler = PrepararConsulta(id, estado);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.Handle(new GetLicitacionByIdQuery(id, "Proveedor")));
    }

    [Fact]
    public async Task ObtenerPorId_ProveedorConLicitacionAbierta_LaDevuelve()
    {
        var id = Guid.NewGuid();
        var handler = PrepararConsulta(id, EstadoLicitacion.Abierta);

        var dto = await handler.Handle(new GetLicitacionByIdQuery(id, "Proveedor"));

        Assert.Equal(id, dto.LicitacionId);
    }

    [Theory]
    [InlineData("Comprador")]
    [InlineData("Admin")]
    [InlineData(null)]
    public async Task ObtenerPorId_CompradorAdminOSinRol_VeLicitacionesAdjudicadas(string? rol)
    {
        var id = Guid.NewGuid();
        var handler = PrepararConsulta(id, EstadoLicitacion.Adjudicada);

        var dto = await handler.Handle(new GetLicitacionByIdQuery(id, rol));

        Assert.Equal(id, dto.LicitacionId);
    }

    [Fact]
    public void OrdenesCompra_TieneIndiceUnicoPorLicitacionId()
    {
        var opciones = new DbContextOptionsBuilder<LicitacionesDbContext>()
            .UseInMemoryDatabase($"IndiceUnico_{Guid.NewGuid()}")
            .Options;
        using var db = new LicitacionesDbContext(opciones);

        var entidad = db.Model.FindEntityType(typeof(OrdenCompra))!;

        Assert.Contains(entidad.GetIndexes(), i =>
            i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == nameof(OrdenCompra.LicitacionId));
    }
}
