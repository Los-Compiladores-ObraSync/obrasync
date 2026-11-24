using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;
using ObraSync.Recepcion.Domain.Constants;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Domain.Recepciones;
using ObraSync.Recepcion.Infrastructure.Persistence;
using ObraSync.Recepcion.Tests.Domain;
using Xunit;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests;

public class EntregaConProblemaTests : IClassFixture<RecepcionApiFactory>
{
    private readonly RecepcionApiFactory _factory;

    public EntregaConProblemaTests(RecepcionApiFactory factory)
    {
        _factory = factory;
    }

    private static RecepcionDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<RecepcionDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RecepcionDbContext(options);
    }

    private async Task<(OrdenCompra oc, Guid obraId, Guid proveedorId, Guid articuloId, PartidaPresupuestaria partida)>
        SembrarOrdenConPartidaEnFactoryAsync()
    {
        var obraId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var ordenId = Guid.NewGuid();

        var partida = new PartidaPresupuestaria
        {
            Id = Guid.NewGuid(),
            ObraId = obraId,
            Etapa = "Albañilería",
            CodigoPartida = $"ALB-{Guid.NewGuid():N}"[..16],
            Descripcion = "Partida de prueba #217",
            MontoAsignado = 100_000m,
            MontoComprometido = 10_000m,
            MontoEjecutado = 0m,
            CreatedAt = DateTime.UtcNow
        };

        var oc = OrdenCompra.Crear(
            ordenId,
            obraId,
            proveedorId,
            10_000m,
            new[] { OrdenCompraItem.Crear(articuloId, 100m, "BOLSA", 100m, "Cemento 25kg") },
            etapa: "Albañilería",
            fechaDespacho: DateTime.UtcNow.AddHours(-2),
            fechaEstimadaLlegada: DateTime.UtcNow.AddDays(2),
            observacionDespacho: "Sale a primera hora");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
        db.PartidasPresupuestarias.Add(partida);
        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();

        return (oc, obraId, proveedorId, articuloId, partida);
    }

    private HttpClient CrearCliente(string role = "Capataz", Guid? userId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            RecepcionApiFactory.CreateToken(role: role, userId: userId));
        return client;
    }

    // ── Reglas de negocio (cero con motivo sí / cero sin motivo no) ─────────

    [Theory]
    [InlineData(MotivosProblemaRecepcion.NoLlegoNada)]
    [InlineData(MotivosProblemaRecepcion.LlegoIncompleta)]
    [InlineData(MotivosProblemaRecepcion.LlegoDanada)]
    [InlineData(MotivosProblemaRecepcion.NoEsLoPedido)]
    public async Task PostRecepciones_Json_RechazadaConMotivoYCantidadesEnCero_Devuelve201(string motivo)
    {
        var (oc, obraId, _, articuloId, partida) = await SembrarOrdenConPartidaEnFactoryAsync();
        using var client = CrearCliente("Capataz");

        var body = new
        {
            obraId,
            ordenCompraId = oc.Id,
            numeroRemito = "",
            fechaRecepcion = DateTime.UtcNow,
            estado = EstadoRecepcion.Rechazada,
            motivoProblema = motivo,
            observaciones = "El transporte tuvo inconvenientes",
            detalles = new[]
            {
                new
                {
                    articuloId,
                    descripcionArticulo = "Cemento 25kg",
                    cantidadEsperada = 100m,
                    cantidadRecibida = 0m,
                    unidadMedida = "BOLSA",
                    conforme = false,
                    observacion = (string?)null
                }
            }
        };

        var resp = await client.PostAsJsonAsync("/api/recepciones", body);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<RecepcionDto>();
        Assert.NotNull(dto);
        Assert.Equal(EstadoRecepcion.Rechazada, dto!.Estado);
        Assert.Equal(motivo, dto.MotivoProblema);
        Assert.Equal("El transporte tuvo inconvenientes", dto.Observaciones);
        Assert.Empty(dto.Fotos);

        // No toca presupuesto
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
        var partidaEnBd = await db.PartidasPresupuestarias.AsNoTracking().SingleAsync(p => p.Id == partida.Id);
        Assert.Equal(0m, partidaEnBd.MontoEjecutado);
    }

    [Fact]
    public async Task PostRecepciones_Json_NormalConTodoEnCeroSinMotivo_Devuelve400()
    {
        var (oc, obraId, _, articuloId, _) = await SembrarOrdenConPartidaEnFactoryAsync();
        using var client = CrearCliente("Capataz");

        var body = new
        {
            obraId,
            ordenCompraId = oc.Id,
            numeroRemito = "REM-CERO-1",
            fechaRecepcion = DateTime.UtcNow,
            detalles = new[]
            {
                new
                {
                    articuloId,
                    descripcionArticulo = "Cemento 25kg",
                    cantidadEsperada = 100m,
                    cantidadRecibida = 0m,
                    unidadMedida = "BOLSA",
                    conforme = false,
                    observacion = (string?)null
                }
            }
        };

        var resp = await client.PostAsJsonAsync("/api/recepciones", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PostRecepciones_Json_RechazadaSinMotivo_Devuelve400()
    {
        var (oc, obraId, _, articuloId, _) = await SembrarOrdenConPartidaEnFactoryAsync();
        using var client = CrearCliente("Capataz");

        var body = new
        {
            obraId,
            ordenCompraId = oc.Id,
            numeroRemito = "REM-RECH-SIN-MOT",
            fechaRecepcion = DateTime.UtcNow,
            estado = EstadoRecepcion.Rechazada,
            motivoProblema = (string?)null,
            detalles = new[]
            {
                new
                {
                    articuloId,
                    descripcionArticulo = "Cemento 25kg",
                    cantidadEsperada = 100m,
                    cantidadRecibida = 0m,
                    unidadMedida = "BOLSA",
                    conforme = false,
                    observacion = (string?)null
                }
            }
        };

        var resp = await client.PostAsJsonAsync("/api/recepciones", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PostRecepciones_Json_RechazadaConMotivoFueraDeCatalogo_Devuelve400()
    {
        var (oc, obraId, _, articuloId, _) = await SembrarOrdenConPartidaEnFactoryAsync();
        using var client = CrearCliente("Capataz");

        var body = new
        {
            obraId,
            ordenCompraId = oc.Id,
            numeroRemito = "REM-MOT-INV",
            fechaRecepcion = DateTime.UtcNow,
            estado = EstadoRecepcion.Rechazada,
            motivoProblema = "Motivo inventado",
            detalles = new[]
            {
                new
                {
                    articuloId,
                    descripcionArticulo = "Cemento 25kg",
                    cantidadEsperada = 100m,
                    cantidadRecibida = 0m,
                    unidadMedida = "BOLSA",
                    conforme = false,
                    observacion = (string?)null
                }
            }
        };

        var resp = await client.PostAsJsonAsync("/api/recepciones", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PostRecepciones_Json_NormalConMotivoProblema_Devuelve400()
    {
        var (oc, obraId, _, articuloId, _) = await SembrarOrdenConPartidaEnFactoryAsync();
        using var client = CrearCliente("Capataz");

        var body = new
        {
            obraId,
            ordenCompraId = oc.Id,
            numeroRemito = "REM-NORM-MOT",
            fechaRecepcion = DateTime.UtcNow,
            motivoProblema = MotivosProblemaRecepcion.NoLlegoNada,
            detalles = new[]
            {
                new
                {
                    articuloId,
                    descripcionArticulo = "Cemento 25kg",
                    cantidadEsperada = 100m,
                    cantidadRecibida = 0m,
                    unidadMedida = "BOLSA",
                    conforme = false,
                    observacion = (string?)null
                }
            }
        };

        var resp = await client.PostAsJsonAsync("/api/recepciones", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── Remanente, presupuesto y entregas posteriores ────────────────────────

    [Fact]
    public async Task Rechazada_NoCambiaRemanenteNiPresupuesto_YPermiteEntregasPosteriores()
    {
        await using var db = CreateInMemoryContext($"Prob_Remanente_{Guid.NewGuid()}");
        var obraId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var ocId = Guid.NewGuid();

        var partida = new PartidaPresupuestaria
        {
            Id = Guid.NewGuid(),
            ObraId = obraId,
            Etapa = "Obra gruesa",
            CodigoPartida = "OG-217",
            Descripcion = "Obra gruesa",
            MontoAsignado = 50_000m,
            MontoComprometido = 10_000m,
            MontoEjecutado = 0m,
            CreatedAt = DateTime.UtcNow
        };
        var oc = OrdenCompra.Crear(
            ocId, obraId, proveedorId, 10_000m,
            new[] { OrdenCompraItem.Crear(articuloId, 100m, "BOLSA", 100m, "Cemento 25kg") },
            etapa: "Obra gruesa",
            fechaDespacho: DateTime.UtcNow);

        db.PartidasPresupuestarias.Add(partida);
        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();

        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var crearHandler = new CrearRecepcionCommandHandler(db, publisher, NullLogger<CrearRecepcionCommandHandler>.Instance);
        var pendientesHandler = new ObtenerOrdenesPendientesQueryHandler(db);

        // 1. Primera entrega parcial: 40 bolsas
        var parcial = await crearHandler.Handle(new CrearRecepcionCommand(
            obraId, ocId, Guid.NewGuid(), "REM-PARC-1", DateTime.UtcNow.AddMinutes(-20), null,
            new[]
            {
                new CrearDetalleRecepcionRequest
                {
                    ArticuloId = articuloId,
                    DescripcionArticulo = "Cemento 25kg",
                    CantidadEsperada = 100m,
                    CantidadRecibida = 40m,
                    UnidadMedida = "BOLSA",
                    Conforme = true
                }
            }));

        Assert.Equal(EstadoRecepcion.Parcial, parcial.Estado);
        Assert.Equal(4_000m, (await db.PartidasPresupuestarias.AsNoTracking().SingleAsync(p => p.Id == partida.Id)).MontoEjecutado);

        // 2. Entrega con problema: no llegó nada (0 bolsas)
        await RedespachoDePrueba.RegistrarAsync(db, ocId);

        var problema = await crearHandler.Handle(new CrearRecepcionCommand(
            obraId, ocId, Guid.NewGuid(), "", DateTime.UtcNow.AddMinutes(-10), "No vino el fletero",
            Array.Empty<CrearDetalleRecepcionRequest>(),
            Estado: EstadoRecepcion.Rechazada,
            MotivoProblema: MotivosProblemaRecepcion.NoLlegoNada,
            Fotos: new[] { new FotoEvidenciaEntrada("porton.jpg", ValidadorFotoEvidenciaTests.CrearJpegValido()) }));

        Assert.Equal(EstadoRecepcion.Rechazada, problema.Estado);
        Assert.Single(problema.Fotos);

        // Remanente intacto (60 pendientes, 40 recibidas) y presupuesto intacto (4_000m ejecutado)
        await RedespachoDePrueba.RegistrarAsync(db, ocId);

        var pendientesTrasProblema = await pendientesHandler.Handle(new ObtenerOrdenesPendientesQuery(obraId));
        var ordenPendiente = Assert.Single(pendientesTrasProblema.Items);
        Assert.True(ordenPendiente.TieneProblemaAbierto);
        var itemPendiente = Assert.Single(ordenPendiente.Items);
        Assert.Equal(100m, itemPendiente.CantidadEsperada);
        Assert.Equal(40m, itemPendiente.CantidadRecibida);
        Assert.Equal(60m, itemPendiente.CantidadPendiente);
        Assert.Equal(4_000m, (await db.PartidasPresupuestarias.AsNoTracking().SingleAsync(p => p.Id == partida.Id)).MontoEjecutado);

        // 3. Entrega final por las 60 bolsas restantes: completa la orden y cierra problema abierto
        var completa = await crearHandler.Handle(new CrearRecepcionCommand(
            obraId, ocId, Guid.NewGuid(), "REM-FINAL-2", DateTime.UtcNow, "Llegó el remanente completo",
            new[]
            {
                new CrearDetalleRecepcionRequest
                {
                    ArticuloId = articuloId,
                    DescripcionArticulo = "Cemento 25kg",
                    CantidadEsperada = 60m,
                    CantidadRecibida = 60m,
                    UnidadMedida = "BOLSA",
                    Conforme = true
                }
            }));

        Assert.Equal(EstadoRecepcion.Completa, completa.Estado);
        Assert.Equal(10_000m, (await db.PartidasPresupuestarias.AsNoTracking().SingleAsync(p => p.Id == partida.Id)).MontoEjecutado);

        var pendientesFinal = await pendientesHandler.Handle(new ObtenerOrdenesPendientesQuery(obraId));
        Assert.Empty(pendientesFinal.Items);

        // Verificar eventos publicados: 2 confirmadas (parcial + completa) y 1 problema (exactamente una vez!)
        await publisher.Received(1).PublishRecepcionConProblemaAsync(
            Arg.Is<RecepcionEntity>(r => r.Id == problema.Id && r.MotivoProblema == MotivosProblemaRecepcion.NoLlegoNada),
            1,
            Arg.Any<Guid>(),
            proveedorId,
            Arg.Any<CancellationToken>());
        await publisher.Received(2).PublishRecepcionConfirmadaAsync(
            Arg.Any<RecepcionEntity>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    // ── Outbox transaccional y publicación única del evento nuevo ────────────

    [Fact]
    public async Task EntregaConProblema_PublicaEventoUnaSolaVezAntesDeGuardar_YNoPublicaConfirmada()
    {
        await using var db = CreateInMemoryContext($"Prob_Outbox_{Guid.NewGuid()}");
        var obraId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var oc = OrdenCompra.Crear(
            Guid.NewGuid(), obraId, proveedorId, 5_000m,
            new[] { OrdenCompraItem.Crear(articuloId, 10m, "UN", 500m, "Caño") },
            fechaDespacho: DateTime.UtcNow);
        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();

        var publisher = Substitute.For<IRecepcionEventPublisher>();
        var correlationId = Guid.NewGuid();
        var ordenLlamadas = new List<string>();

        publisher.When(p => p.PublishRecepcionConProblemaAsync(
                Arg.Any<RecepcionEntity>(),
                Arg.Any<int>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                ordenLlamadas.Add("PublishProblema");
                // Todavía en estado Added en el ChangeTracker (Outbox transaccional)
                Assert.Equal(EntityState.Added, db.Entry(db.Recepciones.Local.Single()).State);
            });

        var handler = new CrearRecepcionCommandHandler(db, publisher, NullLogger<CrearRecepcionCommandHandler>.Instance);

        var dto = await handler.Handle(new CrearRecepcionCommand(
            obraId, oc.Id, Guid.NewGuid(), "", DateTime.UtcNow, "Cajas rotas",
            Array.Empty<CrearDetalleRecepcionRequest>(),
            correlationId,
            EstadoRecepcion.Rechazada,
            MotivosProblemaRecepcion.LlegoDanada,
            new[]
            {
                new FotoEvidenciaEntrada("f1.jpg", ValidadorFotoEvidenciaTests.CrearJpegValido()),
                new FotoEvidenciaEntrada("f2.png", ValidadorFotoEvidenciaTests.CrearPngValido())
            }));

        Assert.Single(ordenLlamadas);
        Assert.Equal(EntityState.Unchanged, db.Entry(db.Recepciones.Local.Single()).State);

        await publisher.Received(1).PublishRecepcionConProblemaAsync(
            Arg.Is<RecepcionEntity>(r =>
                r.Id == dto.Id &&
                r.Estado == EstadoRecepcion.Rechazada &&
                r.MotivoProblema == MotivosProblemaRecepcion.LlegoDanada &&
                r.Observaciones == "Cajas rotas"),
            2,
            correlationId,
            proveedorId,
            Arg.Any<CancellationToken>());

        await publisher.DidNotReceiveWithAnyArgs().PublishRecepcionConfirmadaAsync(default!, default, default);
    }

    [Fact]
    public async Task EntregaConProblema_SiFallaPublicarEvento_NoSePersisteRecepcionNiFotos()
    {
        await using var db = CreateInMemoryContext($"Prob_FailOutbox_{Guid.NewGuid()}");
        var obraId = Guid.NewGuid();
        var oc = OrdenCompra.Crear(
            Guid.NewGuid(), obraId, Guid.NewGuid(), 5_000m,
            new[] { OrdenCompraItem.Crear(Guid.NewGuid(), 10m, "UN", 500m, "Caño") },
            fechaDespacho: DateTime.UtcNow);
        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();

        var publisher = Substitute.For<IRecepcionEventPublisher>();
        publisher.PublishRecepcionConProblemaAsync(
                Arg.Any<RecepcionEntity>(),
                Arg.Any<int>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Fallo en bus")));

        var handler = new CrearRecepcionCommandHandler(db, publisher, NullLogger<CrearRecepcionCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new CrearRecepcionCommand(
                obraId, oc.Id, Guid.NewGuid(), "", DateTime.UtcNow, "Detalle",
                Array.Empty<CrearDetalleRecepcionRequest>(),
                Estado: EstadoRecepcion.Rechazada,
                MotivoProblema: MotivosProblemaRecepcion.NoLlegoNada,
                Fotos: new[] { new FotoEvidenciaEntrada("f1.jpg", ValidadorFotoEvidenciaTests.CrearJpegValido()) })));

        Assert.Equal(0, await db.Recepciones.CountAsync());
        Assert.Equal(0, await db.FotosRecepcion.CountAsync());
    }

    // ── Multipart POST /api/recepciones/problemas y validación de fotos ──────

    [Fact]
    public async Task PostProblemas_Multipart_Con3FotosJpegPngWebp_Devuelve201YGuardaEnDb()
    {
        var (oc, obraId, _, _, _) = await SembrarOrdenConPartidaEnFactoryAsync();
        using var client = CrearCliente("Capataz");

        var jpeg = ValidadorFotoEvidenciaTests.CrearJpegValido(120);
        var png = ValidadorFotoEvidenciaTests.CrearPngValido(140);
        var webp = ValidadorFotoEvidenciaTests.CrearWebpValido(160);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(obraId.ToString()), "obraId");
        form.Add(new StringContent(oc.Id.ToString()), "ordenCompraId");
        form.Add(new StringContent(MotivosProblemaRecepcion.LlegoDanada), "motivo");
        form.Add(new StringContent("Bolsas mojadas por lluvia"), "detalle");

        // Mandamos Content-Type genérico para comprobar que se detecta por magic bytes, no por header
        var c1 = new ByteArrayContent(jpeg);
        c1.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(c1, "fotos", "foto1.jpg");

        var c2 = new ByteArrayContent(png);
        c2.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(c2, "fotos", "foto2.png");

        var c3 = new ByteArrayContent(webp);
        c3.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(c3, "fotos", "foto3.webp");

        var resp = await client.PostAsync("/api/recepciones/problemas", form);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<RecepcionDto>();
        Assert.NotNull(dto);
        Assert.Equal(EstadoRecepcion.Rechazada, dto!.Estado);
        Assert.Equal(MotivosProblemaRecepcion.LlegoDanada, dto.MotivoProblema);
        Assert.Equal("Bolsas mojadas por lluvia", dto.Observaciones);
        Assert.Equal(3, dto.Fotos.Count);
        Assert.Equal("image/jpeg", dto.Fotos[0].ContentType);
        Assert.Equal("image/png", dto.Fotos[1].ContentType);
        Assert.Equal("image/webp", dto.Fotos[2].ContentType);
        Assert.All(dto.Fotos, f => Assert.StartsWith($"/api/recepciones/{dto.Id}/fotos/", f.Url));

        // Verificar persistencia binaria en tabla FotosRecepcion
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
        var fotosEnBd = await db.FotosRecepcion
            .Where(f => f.RecepcionId == dto.Id)
            .OrderBy(f => f.Orden)
            .ToListAsync();
        Assert.Equal(3, fotosEnBd.Count);
        Assert.Equal(jpeg, fotosEnBd[0].Contenido);
        Assert.Equal(png, fotosEnBd[1].Contenido);
        Assert.Equal(webp, fotosEnBd[2].Contenido);
    }

    [Fact]
    public async Task PostProblemas_Multipart_MasDe3Fotos_Devuelve400()
    {
        var (oc, obraId, _, _, _) = await SembrarOrdenConPartidaEnFactoryAsync();
        using var client = CrearCliente("Capataz");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(obraId.ToString()), "obraId");
        form.Add(new StringContent(oc.Id.ToString()), "ordenCompraId");
        form.Add(new StringContent(MotivosProblemaRecepcion.LlegoDanada), "motivo");

        for (int i = 1; i <= 4; i++)
        {
            form.Add(new ByteArrayContent(ValidadorFotoEvidenciaTests.CrearJpegValido()), "fotos", $"f{i}.jpg");
        }

        var resp = await client.PostAsync("/api/recepciones/problemas", form);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PostProblemas_Multipart_FotoFormatoInvalidoDisfrazadoDeJpg_Devuelve400()
    {
        var (oc, obraId, _, _, _) = await SembrarOrdenConPartidaEnFactoryAsync();
        using var client = CrearCliente("Capataz");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(obraId.ToString()), "obraId");
        form.Add(new StringContent(oc.Id.ToString()), "ordenCompraId");
        form.Add(new StringContent(MotivosProblemaRecepcion.NoEsLoPedido), "motivo");

        var falso = new ByteArrayContent(Encoding.UTF8.GetBytes("<html>esto no es una imagen</html>"));
        falso.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(falso, "fotos", "falso.jpg");

        var resp = await client.PostAsync("/api/recepciones/problemas", form);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PostProblemas_Multipart_FotoMayorA2Mb_Devuelve400()
    {
        var (oc, obraId, _, _, _) = await SembrarOrdenConPartidaEnFactoryAsync();
        using var client = CrearCliente("Capataz");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(obraId.ToString()), "obraId");
        form.Add(new StringContent(oc.Id.ToString()), "ordenCompraId");
        form.Add(new StringContent(MotivosProblemaRecepcion.LlegoDanada), "motivo");

        var grande = new ByteArrayContent(ValidadorFotoEvidenciaTests.CrearJpegValido(ValidadorFotoEvidencia.MaxTamanoFotoBytes + 1));
        grande.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(grande, "fotos", "grande.jpg");

        var resp = await client.PostAsync("/api/recepciones/problemas", form);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PostProblemas_Multipart_DetalleSupera500Caracteres_Devuelve400()
    {
        var (oc, obraId, _, _, _) = await SembrarOrdenConPartidaEnFactoryAsync();
        using var client = CrearCliente("Capataz");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(obraId.ToString()), "obraId");
        form.Add(new StringContent(oc.Id.ToString()), "ordenCompraId");
        form.Add(new StringContent(MotivosProblemaRecepcion.NoLlegoNada), "motivo");
        form.Add(new StringContent(new string('A', 501)), "detalle");

        var resp = await client.PostAsync("/api/recepciones/problemas", form);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Theory]
    [InlineData("Comprador")]
    [InlineData("Proveedor")]
    public async Task PostProblemas_RolesNoAutorizados_Devuelve403(string rol)
    {
        var (oc, obraId, _, _, _) = await SembrarOrdenConPartidaEnFactoryAsync();
        using var client = CrearCliente(rol);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(obraId.ToString()), "obraId");
        form.Add(new StringContent(oc.Id.ToString()), "ordenCompraId");
        form.Add(new StringContent(MotivosProblemaRecepcion.NoLlegoNada), "motivo");

        var resp = await client.PostAsync("/api/recepciones/problemas", form);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task PostProblemas_DosProblemasSeguidosSinRemito_AmbosDevuelven201SinColisionDeIndice()
    {
        var (oc, obraId, _, _, _) = await SembrarOrdenConPartidaEnFactoryAsync();
        using var client = CrearCliente("Capataz");

        for (int i = 0; i < 2; i++)
        {
            if (i > 0) await RedespachoDePrueba.RegistrarAsync(_factory.Services, oc.Id);

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(obraId.ToString()), "obraId");
            form.Add(new StringContent(oc.Id.ToString()), "ordenCompraId");
            form.Add(new StringContent(MotivosProblemaRecepcion.NoLlegoNada), "motivo");
            form.Add(new StringContent($"Intento fallido #{i + 1}"), "detalle");

            var resp = await client.PostAsync("/api/recepciones/problemas", form);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        }
    }

    [Fact]
    public async Task FlujoParcialProblemaYCompleta_PublicaOrdenCompletaFalseLuegoProblemaYLuegoOrdenCompletaTrue()
    {
        await using var db = CreateInMemoryContext($"Prob_OrdenCompleta_{Guid.NewGuid()}");
        var obraId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var oc = OrdenCompra.Crear(
            Guid.NewGuid(), obraId, proveedorId, 10_000m,
            new[] { OrdenCompraItem.Crear(articuloId, 100m, "BOLSA", 100m, "Cemento 25kg") },
            fechaDespacho: DateTime.UtcNow);
        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();

        var endpoint = Substitute.For<MassTransit.IPublishEndpoint>();
        var confirmadas = new List<ObraSync.Contracts.Events.RecepcionConfirmadaEvent>();
        var problemas = new List<ObraSync.Contracts.Events.RecepcionConProblemaEvent>();

        endpoint.Publish(
                Arg.Do<ObraSync.Contracts.Events.RecepcionConfirmadaEvent>(e => confirmadas.Add(e)),
                Arg.Any<MassTransit.IPipe<MassTransit.PublishContext<ObraSync.Contracts.Events.RecepcionConfirmadaEvent>>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        endpoint.Publish(
                Arg.Do<ObraSync.Contracts.Events.RecepcionConProblemaEvent>(e => problemas.Add(e)),
                Arg.Any<MassTransit.IPipe<MassTransit.PublishContext<ObraSync.Contracts.Events.RecepcionConProblemaEvent>>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var publisher = new ObraSync.Recepcion.Infrastructure.Messaging.MassTransitRecepcionEventPublisher(endpoint);
        var handler = new CrearRecepcionCommandHandler(db, publisher, NullLogger<CrearRecepcionCommandHandler>.Instance);

        // 1. Parcial (40/100) -> OrdenCompleta = false
        await handler.Handle(new CrearRecepcionCommand(
            obraId, oc.Id, Guid.NewGuid(), "R-1", DateTime.UtcNow.AddMinutes(-5), null,
            new[]
            {
                new CrearDetalleRecepcionRequest
                {
                    ArticuloId = articuloId,
                    DescripcionArticulo = "Cemento 25kg",
                    CantidadEsperada = 100m,
                    CantidadRecibida = 40m,
                    UnidadMedida = "BOLSA",
                    Conforme = true
                }
            }));

        // 2. Problema (Rechazada) -> publica RecepcionConProblemaEvent (no confirmada)
        await RedespachoDePrueba.RegistrarAsync(db, oc.Id);

        await handler.Handle(new CrearRecepcionCommand(
            obraId, oc.Id, Guid.NewGuid(), "", DateTime.UtcNow.AddMinutes(-2), "No llegó el segundo viaje",
            Array.Empty<CrearDetalleRecepcionRequest>(),
            Estado: EstadoRecepcion.Rechazada,
            MotivoProblema: MotivosProblemaRecepcion.NoLlegoNada));

        // 3. Remanente completo (60/60) -> OrdenCompleta = true
        await RedespachoDePrueba.RegistrarAsync(db, oc.Id);

        await handler.Handle(new CrearRecepcionCommand(
            obraId, oc.Id, Guid.NewGuid(), "R-2", DateTime.UtcNow, null,
            new[]
            {
                new CrearDetalleRecepcionRequest
                {
                    ArticuloId = articuloId,
                    DescripcionArticulo = "Cemento 25kg",
                    CantidadEsperada = 60m,
                    CantidadRecibida = 60m,
                    UnidadMedida = "BOLSA",
                    Conforme = true
                }
            }));

        Assert.Equal(2, confirmadas.Count);
        Assert.False(confirmadas[0].OrdenCompleta);
        Assert.True(confirmadas[1].OrdenCompleta);

        var problemaEvt = Assert.Single(problemas);
        Assert.Equal(oc.Id, problemaEvt.OrdenCompraId);
        Assert.Equal(MotivosProblemaRecepcion.NoLlegoNada, problemaEvt.Motivo);
        Assert.Equal(0, problemaEvt.CantidadFotos);
        Assert.Equal(obraId, problemaEvt.ObraId);
        Assert.Equal(proveedorId, problemaEvt.ProveedorId);
    }

    [Fact]
    public void EndpointProblemas_TieneLimiteDeTamanoDe8MbConfigurado()
    {
        var metodo = typeof(ObraSync.Recepcion.Api.Controllers.RecepcionesController)
            .GetMethod(nameof(ObraSync.Recepcion.Api.Controllers.RecepcionesController.RegistrarEntregaConProblema));

        Assert.NotNull(metodo);

        var requestSizeLimit = metodo!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute), inherit: false)
            .Cast<Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute>()
            .SingleOrDefault();
        Assert.NotNull(requestSizeLimit);

        var formLimits = metodo
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute), inherit: false)
            .Cast<Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute>()
            .SingleOrDefault();
        Assert.NotNull(formLimits);
        Assert.Equal(ValidadorFotoEvidencia.MaxTamanoSolicitudMultipartBytes, formLimits!.MultipartBodyLengthLimit);
    }
}
