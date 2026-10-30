using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ObraSync.Recepcion.Api.Controllers;
using ObraSync.Recepcion.Api.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Infrastructure.Persistence;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests;

public class CrearRecepcionPublicacionTests
{
    private static (RecepcionesController controller, IRecepcionEventPublisher publisher, CrearRecepcionRequest request, RecepcionDbContext db)
        Preparar(string? correlationHeader)
    {
        var db = new RecepcionDbContext(new DbContextOptionsBuilder<RecepcionDbContext>()
            .UseInMemoryDatabase($"Pub_{Guid.NewGuid()}").Options);

        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var oc = OrdenCompra.Crear(Guid.NewGuid(), obraId, Guid.NewGuid(), 1000,
            new List<OrdenCompraItem> { OrdenCompraItem.Crear(articuloId, 100, "UNIDAD", 10, "Hierro") });
        db.OrdenesCompra.Add(oc);
        db.SaveChanges();

        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var controller = new RecepcionesController(db, NullLogger<RecepcionesController>.Instance, publisher);

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"))
        };
        if (correlationHeader is not null)
        {
            http.Request.Headers["X-Correlation-ID"] = correlationHeader;
        }
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        var request = new CrearRecepcionRequest
        {
            ObraId = obraId,
            OrdenCompraId = oc.Id,
            NumeroRemito = "REM-1",
            FechaRecepcion = DateTime.UtcNow,
            Detalles = new List<CrearDetalleRecepcionRequest>
            {
                new()
                {
                    ArticuloId = articuloId,
                    DescripcionArticulo = "Hierro",
                    CantidadEsperada = 100,
                    CantidadRecibida = 100,
                    UnidadMedida = "UNIDAD",
                    Conforme = true
                }
            }
        };
        return (controller, publisher, request, db);
    }

    [Fact]
    public async Task Header_valido_se_pasa_al_publisher_y_se_publica_una_vez()
    {
        var correlationId = Guid.NewGuid();
        var (controller, publisher, request, _) = Preparar(correlationId.ToString());

        var result = await controller.CrearRecepcion(request, CancellationToken.None);

        Assert.Equal(201, Assert.IsType<ObjectResult>(result).StatusCode);
        await publisher.Received(1).PublishRecepcionConfirmadaAsync(
            Arg.Is<RecepcionEntity>(r => r.NumeroRemito == "REM-1"),
            correlationId,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("no-es-un-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Header_invalido_o_ausente_genera_un_correlationId_nuevo(string? header)
    {
        var (controller, publisher, request, _) = Preparar(header);

        await controller.CrearRecepcion(request, CancellationToken.None);

        await publisher.Received(1).PublishRecepcionConfirmadaAsync(
            Arg.Any<RecepcionEntity>(),
            Arg.Is<Guid>(g => g != Guid.Empty),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Si_publicar_falla_la_recepcion_queda_guardada_y_devuelve_201()
    {
        var (controller, publisher, request, db) = Preparar(Guid.NewGuid().ToString());
        publisher.PublishRecepcionConfirmadaAsync(default!, default, default)
            .ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("rabbit caído")));

        var result = await controller.CrearRecepcion(request, CancellationToken.None);

        Assert.Equal(201, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal(1, await db.Recepciones.CountAsync());
    }

    [Fact]
    public async Task Si_la_validacion_falla_no_se_publica()
    {
        var (controller, publisher, request, _) = Preparar(null);
        request.NumeroRemito = "";

        var result = await controller.CrearRecepcion(request, CancellationToken.None);

        Assert.Equal(400, Assert.IsType<ObjectResult>(result).StatusCode);
        await publisher.DidNotReceiveWithAnyArgs().PublishRecepcionConfirmadaAsync(default!, default, default);
    }
}

