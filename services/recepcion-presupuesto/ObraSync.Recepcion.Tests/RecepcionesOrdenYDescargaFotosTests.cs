using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Constants;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Persistence;
using ObraSync.Recepcion.Tests.Domain;
using Xunit;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests;

/// <summary>
/// Pruebas de integración para lectura de recepciones por orden (GET /api/recepciones/ordenes/{ordenCompraId})
/// y descarga autenticada de fotos de evidencia (GET /api/recepciones/{id}/fotos/{fotoId}),
/// incluyendo permisos por rol (Capataz de la obra vs otra obra, Compras, Proveedor dueño vs otro Proveedor)
/// y encabezados de caché privado (#217).
/// </summary>
public class RecepcionesOrdenYDescargaFotosTests
{
    private sealed class FactoryConValidador : RecepcionApiFactory
    {
        public IObraAsignacionValidator Validador { get; } = Substitute.For<IObraAsignacionValidator>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IObraAsignacionValidator>();
                services.AddSingleton(Validador);
            });
        }
    }

    private static HttpClient CrearCliente(FactoryConValidador factory, string? role, Guid? userId = null)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                RecepcionApiFactory.CreateToken(role: role, userId: userId));
        }
        return client;
    }

    private static async Task<(OrdenCompra oc, RecepcionEntity recProblema, FotoRecepcion foto, byte[] bytesFoto)>
        SembrarOrdenConProblemaYFotoAsync(FactoryConValidador factory)
    {
        var obraId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var ordenId = Guid.NewGuid();
        var recepcionParcialId = Guid.NewGuid();
        var recepcionProblemaId = Guid.NewGuid();
        var fotoId = Guid.NewGuid();
        var bytesFoto = ValidadorFotoEvidenciaTests.CrearPngValido(256);

        var oc = OrdenCompra.Crear(
            ordenId,
            obraId,
            proveedorId,
            20_000m,
            new[] { OrdenCompraItem.Crear(articuloId, 100m, "BOLSA", 200m, "Cemento 25kg") },
            etapa: "Estructura",
            fechaDespacho: new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc),
            fechaEstimadaLlegada: new DateTime(2026, 11, 22, 15, 0, 0, DateTimeKind.Utc),
            observacionDespacho: "Entregar por portón norte");

        var recParcial = new RecepcionEntity
        {
            Id = recepcionParcialId,
            ObraId = obraId,
            OrdenCompraId = ordenId,
            RecepcionadoPorUsuarioId = Guid.NewGuid(),
            NumeroRemito = "REM-PARC-217",
            FechaRecepcion = new DateTime(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc),
            Estado = EstadoRecepcion.Parcial,
            Observaciones = "Primera mitad",
            CreatedAt = new DateTime(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc),
            Detalles = new List<DetalleRecepcion>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    RecepcionId = recepcionParcialId,
                    ArticuloId = articuloId,
                    DescripcionArticulo = "Cemento 25kg",
                    CantidadEsperada = 100m,
                    CantidadRecibida = 30m,
                    UnidadMedida = "BOLSA",
                    Conforme = true
                }
            }
        };

        var foto = new FotoRecepcion
        {
            Id = fotoId,
            RecepcionId = recepcionProblemaId,
            NombreArchivo = "bolsas-rotas.png",
            ContentType = "image/png",
            TamanoBytes = bytesFoto.LongLength,
            Contenido = bytesFoto,
            Orden = 1,
            CreatedAt = new DateTime(2026, 11, 21, 14, 0, 0, DateTimeKind.Utc)
        };

        var recProblema = new RecepcionEntity
        {
            Id = recepcionProblemaId,
            ObraId = obraId,
            OrdenCompraId = ordenId,
            RecepcionadoPorUsuarioId = Guid.NewGuid(),
            NumeroRemito = "PROB-217-01",
            FechaRecepcion = new DateTime(2026, 11, 21, 14, 0, 0, DateTimeKind.Utc),
            Estado = EstadoRecepcion.Rechazada,
            MotivoProblema = MotivosProblemaRecepcion.LlegoDanada,
            Observaciones = "Pallet roto en la descarga",
            CreatedAt = new DateTime(2026, 11, 21, 14, 0, 0, DateTimeKind.Utc),
            Detalles = new List<DetalleRecepcion>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    RecepcionId = recepcionProblemaId,
                    ArticuloId = articuloId,
                    DescripcionArticulo = "Cemento 25kg",
                    CantidadEsperada = 100m,
                    CantidadRecibida = 0m,
                    UnidadMedida = "BOLSA",
                    Conforme = false
                }
            },
            Fotos = new List<FotoRecepcion> { foto }
        };

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
        db.OrdenesCompra.Add(oc);
        db.Recepciones.AddRange(recParcial, recProblema);
        await db.SaveChangesAsync();

        return (oc, recProblema, foto, bytesFoto);
    }

    // ── Lectura de recepciones por orden: GET /api/recepciones/ordenes/{id} ──

    [Fact]
    public async Task GetRecepcionesPorOrden_ConComprador_DevuelveHistorialDespachoSaldosYFotos()
    {
        await using var factory = new FactoryConValidador();
        var (oc, recProblema, foto, _) = await SembrarOrdenConProblemaYFotoAsync(factory);

        using var client = CrearCliente(factory, "Comprador");
        var resp = await client.GetAsync($"/api/recepciones/ordenes/{oc.Id}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<RecepcionesDeOrdenDto>();
        Assert.NotNull(dto);
        Assert.Equal(oc.Id, dto!.OrdenCompraId);
        Assert.Equal(oc.ObraId, dto.ObraId);
        Assert.Equal(new DateTime(2026, 11, 22, 15, 0, 0, DateTimeKind.Utc), dto.FechaEstimadaLlegada);
        Assert.Equal("Entregar por portón norte", dto.ObservacionDespacho);
        Assert.False(dto.EstaCompleta);
        Assert.True(dto.TieneProblemaAbierto);

        var item = Assert.Single(dto.Items);
        Assert.Equal(100m, item.CantidadEsperada);
        Assert.Equal(30m, item.CantidadRecibida);
        Assert.Equal(70m, item.CantidadPendiente);

        Assert.Equal(2, dto.Recepciones.Count);
        Assert.Equal(EstadoRecepcion.Parcial, dto.Recepciones[0].Estado);
        Assert.Equal(EstadoRecepcion.Rechazada, dto.Recepciones[1].Estado);
        Assert.Equal(MotivosProblemaRecepcion.LlegoDanada, dto.Recepciones[1].MotivoProblema);
        var fotoMeta = Assert.Single(dto.Recepciones[1].Fotos);
        Assert.Equal(foto.Id, fotoMeta.Id);
        Assert.Equal("image/png", fotoMeta.ContentType);
        Assert.Equal($"/api/recepciones/{recProblema.Id}/fotos/{foto.Id}", fotoMeta.Url);
    }

    [Fact]
    public async Task GetRecepcionesPorOrden_ProveedorDueno_Devuelve200_OtroProveedor_Devuelve403()
    {
        await using var factory = new FactoryConValidador();
        var (oc, _, _, _) = await SembrarOrdenConProblemaYFotoAsync(factory);

        using var dueno = CrearCliente(factory, "Proveedor", oc.ProveedorId);
        var respDueno = await dueno.GetAsync($"/api/recepciones/ordenes/{oc.Id}");
        Assert.Equal(HttpStatusCode.OK, respDueno.StatusCode);

        using var ajeno = CrearCliente(factory, "Proveedor", Guid.NewGuid());
        var respAjeno = await ajeno.GetAsync($"/api/recepciones/ordenes/{oc.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, respAjeno.StatusCode);
    }

    [Fact]
    public async Task GetRecepcionesPorOrden_CapatazObraAsignada_200_ObraAjena_403_UsuariosCaido_503()
    {
        await using var factory = new FactoryConValidador();
        var (oc, _, _, _) = await SembrarOrdenConProblemaYFotoAsync(factory);

        factory.Validador.EstaAsignadaACapatazAsync(oc.ObraId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);
        using var client = CrearCliente(factory, "Capataz");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/recepciones/ordenes/{oc.Id}")).StatusCode);

        factory.Validador.EstaAsignadaACapatazAsync(oc.ObraId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(false);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/recepciones/ordenes/{oc.Id}")).StatusCode);

        factory.Validador.EstaAsignadaACapatazAsync(oc.ObraId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ObraAsignacionNoVerificableException("Usuarios caído"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync($"/api/recepciones/ordenes/{oc.Id}")).StatusCode);
    }

    [Fact]
    public async Task GetRecepcionesPorOrden_Inexistente_Devuelve404()
    {
        await using var factory = new FactoryConValidador();
        using var client = CrearCliente(factory, "Admin");

        var resp = await client.GetAsync($"/api/recepciones/ordenes/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── Descarga de fotos: GET /api/recepciones/{id}/fotos/{fotoId} ──────────

    [Theory]
    [InlineData("Comprador")]
    [InlineData("Admin")]
    public async Task DescargarFoto_ComprasYAdmin_DevuelveBytesYCacheControlPrivado(string rol)
    {
        await using var factory = new FactoryConValidador();
        var (_, recProblema, foto, bytesFoto) = await SembrarOrdenConProblemaYFotoAsync(factory);

        using var client = CrearCliente(factory, rol);
        var resp = await client.GetAsync($"/api/recepciones/{recProblema.Id}/fotos/{foto.Id}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("image/png", resp.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(resp.Headers.CacheControl);
        Assert.True(resp.Headers.CacheControl!.Private);
        Assert.Contains("nosniff", resp.Headers.GetValues("X-Content-Type-Options"));
        var descargado = await resp.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytesFoto, descargado);
    }

    [Fact]
    public async Task DescargarFoto_CapatazObraAsignada_Devuelve200_ObraAjena_Devuelve403()
    {
        await using var factory = new FactoryConValidador();
        var (oc, recProblema, foto, bytesFoto) = await SembrarOrdenConProblemaYFotoAsync(factory);

        factory.Validador.EstaAsignadaACapatazAsync(oc.ObraId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        using var capataz = CrearCliente(factory, "Capataz");
        var respOk = await capataz.GetAsync($"/api/recepciones/{recProblema.Id}/fotos/{foto.Id}");
        Assert.Equal(HttpStatusCode.OK, respOk.StatusCode);
        Assert.True(respOk.Headers.CacheControl!.Private);
        Assert.Equal(bytesFoto, await respOk.Content.ReadAsByteArrayAsync());

        factory.Validador.EstaAsignadaACapatazAsync(oc.ObraId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var respForbidden = await capataz.GetAsync($"/api/recepciones/{recProblema.Id}/fotos/{foto.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, respForbidden.StatusCode);
    }

    [Fact]
    public async Task DescargarFoto_ProveedorDueno_Devuelve200_OtroProveedor_Devuelve403()
    {
        await using var factory = new FactoryConValidador();
        var (oc, recProblema, foto, bytesFoto) = await SembrarOrdenConProblemaYFotoAsync(factory);

        using var dueno = CrearCliente(factory, "Proveedor", oc.ProveedorId);
        var respDueno = await dueno.GetAsync($"/api/recepciones/{recProblema.Id}/fotos/{foto.Id}");
        Assert.Equal(HttpStatusCode.OK, respDueno.StatusCode);
        Assert.True(respDueno.Headers.CacheControl!.Private);
        Assert.Equal(bytesFoto, await respDueno.Content.ReadAsByteArrayAsync());

        using var ajeno = CrearCliente(factory, "Proveedor", Guid.NewGuid());
        var respAjeno = await ajeno.GetAsync($"/api/recepciones/{recProblema.Id}/fotos/{foto.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, respAjeno.StatusCode);
    }

    [Fact]
    public async Task DescargarFoto_SinToken_Devuelve401_YFotoInexistente_Devuelve404()
    {
        await using var factory = new FactoryConValidador();
        var (_, recProblema, foto, _) = await SembrarOrdenConProblemaYFotoAsync(factory);

        using var anon = CrearCliente(factory, role: null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/recepciones/{recProblema.Id}/fotos/{foto.Id}")).StatusCode);

        using var admin = CrearCliente(factory, "Admin");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/recepciones/{recProblema.Id}/fotos/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/recepciones/{Guid.NewGuid()}/fotos/{foto.Id}")).StatusCode);
    }

    [Fact]
    public async Task PostProblemas_CapatazObraAjena_Devuelve403_ObraAsignada_Devuelve201()
    {
        await using var factory = new FactoryConValidador();
        var (oc, _, _, _) = await SembrarOrdenConProblemaYFotoAsync(factory);

        using var capataz = CrearCliente(factory, "Capataz");

        factory.Validador.EstaAsignadaACapatazAsync(oc.ObraId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        using (var formForbidden = new MultipartFormDataContent())
        {
            formForbidden.Add(new StringContent(oc.ObraId.ToString()), "obraId");
            formForbidden.Add(new StringContent(oc.Id.ToString()), "ordenCompraId");
            formForbidden.Add(new StringContent(MotivosProblemaRecepcion.NoLlegoNada), "motivo");
            var respForbidden = await capataz.PostAsync("/api/recepciones/problemas", formForbidden);
            Assert.Equal(HttpStatusCode.Forbidden, respForbidden.StatusCode);
        }

        factory.Validador.EstaAsignadaACapatazAsync(oc.ObraId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        using (var formOk = new MultipartFormDataContent())
        {
            formOk.Add(new StringContent(oc.ObraId.ToString()), "obraId");
            formOk.Add(new StringContent(oc.Id.ToString()), "ordenCompraId");
            formOk.Add(new StringContent(MotivosProblemaRecepcion.NoLlegoNada), "motivo");
            var respOk = await capataz.PostAsync("/api/recepciones/problemas", formOk);
            Assert.Equal(HttpStatusCode.Created, respOk.StatusCode);
        }
    }
}
