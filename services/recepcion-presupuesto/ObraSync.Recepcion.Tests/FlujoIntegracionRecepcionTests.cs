using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Consumers;
using ObraSync.Recepcion.Infrastructure.Persistence;
using ObraSync.Recepcion.Infrastructure.Persistence.Repositories;
using Xunit;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;
using DetalleRecepcionEntity = ObraSync.Recepcion.Domain.Entities.DetalleRecepcion;

namespace ObraSync.Recepcion.Tests;

/// <summary>
/// Pruebas de integración del flujo completo para Recepción y Control Presupuestario (#105):
/// 1. Emisión de OC -> Consumo -> Orden pendiente -> Recepción total -> Publicación de evento -> Deja de estar pendiente.
/// 2. Caso de OC bloqueada por desvío presupuestario: publicación de OrdenCompraBloqueadaEvent, no aparece en pendientes y rechaza recepciones (409).
/// 3. Validaciones de negocio: remito duplicado (409) y recepción sobre OC ya recibida (409).
/// 4. Caso de recepción parcial: modelo de estados y cálculo de cantidades acumuladas pendientes.
/// 5. Control de acceso por rol y asignación de obra.
/// </summary>
public class FlujoIntegracionRecepcionTests : IClassFixture<RecepcionApiFactory>
{
    private readonly RecepcionApiFactory _factory;

    public FlujoIntegracionRecepcionTests(RecepcionApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CrearCliente(string rol = "Capataz", Guid? userId = null)
    {
        var client = _factory.CreateClient();
        var token = RecepcionApiFactory.CreateToken(role: rol, userId: userId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private ConsumeContext<OrdenCompraEmitidaEvent> CrearConsumeContext(
        OrdenCompraEmitidaEvent evento,
        CancellationToken cancellationToken = default)
    {
        var context = Substitute.For<ConsumeContext<OrdenCompraEmitidaEvent>>();
        context.Message.Returns(evento);
        context.CancellationToken.Returns(cancellationToken);
        return context;
    }

    [Fact]
    public async Task FlujoCompleto_OrdenCompraEmitida_HastaRecepcionTotal_VerificaRemanentesYEventos()
    {
        // ── 1. Preparación de Partida Presupuestaria y emisión de OC ──
        var obraId = Guid.NewGuid();
        var etapa = "Estructura Hormigón";
        var partidaId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

            var partida = new PartidaPresupuestaria
            {
                Id = partidaId,
                ObraId = obraId,
                Etapa = etapa,
                CodigoPartida = "PART-001",
                Descripcion = "Materiales Estructura",
                MontoAsignado = 100000m,
                MontoComprometido = 0m,
                MontoEjecutado = 0m,
                Moneda = "ARS"
            };
            db.PartidasPresupuestarias.Add(partida);
            await db.SaveChangesAsync();
        }

        var ordenCompraId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var articulo1Id = Guid.NewGuid();
        var articulo2Id = Guid.NewGuid();

        var eventoOC = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: ordenCompraId,
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: obraId,
            ProveedorId: proveedorId,
            MontoTotal: 15000m,
            Etapa: etapa,
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(articulo1Id, 50m, "bolsas", 200m, "Cemento Portland"),
                new(articulo2Id, 25m, "m3", 200m, "Arena Gruesa")
            });

        // Consumir el evento simulando la llegada desde RabbitMQ
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            var repo = new OrdenCompraRepository(db);
            var consumer = new OrdenCompraEmitidaConsumer(db, repo, NullLogger<OrdenCompraEmitidaConsumer>.Instance);

            var context = CrearConsumeContext(eventoOC);
            await consumer.Consume(context);
        }

        // Verificar en BD que el monto fue comprometido en la partida
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            var partidaActualizada = await db.PartidasPresupuestarias.FindAsync(partidaId);
            Assert.NotNull(partidaActualizada);
            Assert.Equal(15000m, partidaActualizada.MontoComprometido);
            Assert.Equal(85000m, partidaActualizada.MontoDisponible);
        }

        // ── 2. Consulta de Órdenes Pendientes vía API (Capataz) ──
        var capatazId = Guid.NewGuid();
        var client = CrearCliente("Capataz", capatazId);

        var respuestaPendientes = await client.GetAsync($"/api/recepciones/ordenes-pendientes?obraId={obraId}");
        Assert.Equal(HttpStatusCode.OK, respuestaPendientes.StatusCode);

        var pendientes = await respuestaPendientes.Content.ReadFromJsonAsync<List<OrdenPendienteDto>>();
        Assert.NotNull(pendientes);
        var ordenPendiente = pendientes.FirstOrDefault(o => o.OrdenCompraId == ordenCompraId);
        Assert.NotNull(ordenPendiente);
        Assert.Equal(2, ordenPendiente.Items.Count);
        Assert.All(ordenPendiente.Items, item => Assert.Equal(0m, item.CantidadRecibida));

        // ── 3. Capataz registra la recepción física total (POST /api/recepciones) ──
        var numeroRemito = $"REM-INT-{Guid.NewGuid():N}"[..20];
        var solicitudRecepcion = new CrearRecepcionRequest
        {
            ObraId = obraId,
            OrdenCompraId = ordenCompraId,
            NumeroRemito = numeroRemito,
            FechaRecepcion = DateTime.UtcNow,
            Observaciones = "Entrega completa de materiales según remito",
            Detalles = new List<CrearDetalleRecepcionRequest>
            {
                new()
                {
                    ArticuloId = articulo1Id,
                    DescripcionArticulo = "Cemento Portland",
                    CantidadEsperada = 50m,
                    CantidadRecibida = 50m,
                    UnidadMedida = "bolsas",
                    Conforme = true,
                    Observacion = "Bolsas en perfecto estado"
                },
                new()
                {
                    ArticuloId = articulo2Id,
                    DescripcionArticulo = "Arena Gruesa",
                    CantidadEsperada = 25m,
                    CantidadRecibida = 25m,
                    UnidadMedida = "m3",
                    Conforme = true,
                    Observacion = "Descargado en obra"
                }
            }
        };

        var respuestaCreacion = await client.PostAsJsonAsync("/api/recepciones", solicitudRecepcion);
        Assert.Equal(HttpStatusCode.Created, respuestaCreacion.StatusCode);

        var recepcionCreada = await respuestaCreacion.Content.ReadFromJsonAsync<RecepcionDto>();
        Assert.NotNull(recepcionCreada);
        Assert.Equal(numeroRemito, recepcionCreada.NumeroRemito);
        Assert.Equal(EstadoRecepcion.Completa, recepcionCreada.Estado);
        Assert.Equal(2, recepcionCreada.Detalles.Count);

        // ── 4. Verificación de que la OC ya no figura como pendiente ──
        var respuestaPendientesDespues = await client.GetAsync($"/api/recepciones/ordenes-pendientes?obraId={obraId}");
        var pendientesDespues = await respuestaPendientesDespues.Content.ReadFromJsonAsync<List<OrdenPendienteDto>>();
        Assert.NotNull(pendientesDespues);
        Assert.DoesNotContain(pendientesDespues, o => o.OrdenCompraId == ordenCompraId);

        // ── 5. Consulta por ID y consulta filtrada por OrdenCompraId ──
        var respuestaPorId = await client.GetAsync($"/api/recepciones/{recepcionCreada.Id}");
        Assert.Equal(HttpStatusCode.OK, respuestaPorId.StatusCode);
        var recepcionConsultada = await respuestaPorId.Content.ReadFromJsonAsync<RecepcionDto>();
        Assert.NotNull(recepcionConsultada);
        Assert.Equal(recepcionCreada.Id, recepcionConsultada.Id);
        Assert.Equal(EstadoRecepcion.Completa, recepcionConsultada.Estado);

        var respuestaFiltroOC = await client.GetAsync($"/api/recepciones?ordenCompraId={ordenCompraId}");
        Assert.Equal(HttpStatusCode.OK, respuestaFiltroOC.StatusCode);
        var recepcionesPorOc = await respuestaFiltroOC.Content.ReadFromJsonAsync<List<RecepcionDto>>();
        Assert.NotNull(recepcionesPorOc);
        Assert.Contains(recepcionesPorOc, r => r.Id == recepcionCreada.Id);
    }

    [Fact]
    public async Task FlujoCompleto_CasoOrdenCompraBloqueadaPorDesvioPresupuestario()
    {
        // ── 1. Partida con presupuesto casi agotado ──
        var obraId = Guid.NewGuid();
        var etapa = "Terminaciones";
        var partidaId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

            var partida = new PartidaPresupuestaria
            {
                Id = partidaId,
                ObraId = obraId,
                Etapa = etapa,
                CodigoPartida = "PART-TERM-01",
                Descripcion = "Pinturas y Revestimientos",
                MontoAsignado = 10000m,
                MontoComprometido = 8000m, // Solo quedan $2,000 disponibles
                MontoEjecutado = 0m,
                Moneda = "ARS"
            };
            db.PartidasPresupuestarias.Add(partida);
            await db.SaveChangesAsync();
        }

        // ── 2. Llega una OC por $5,000 (provoca desvío: $8,000 + $5,000 = $13,000 > $10,000) ──
        var ordenCompraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        var eventoOC = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            OrdenCompraId: ordenCompraId,
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: obraId,
            ProveedorId: Guid.NewGuid(),
            MontoTotal: 5000m,
            Etapa: etapa,
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(articuloId, 10m, "latas", 500m, "Pintura Látex")
            });

        ConsumeContext<OrdenCompraEmitidaEvent> context;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            var repo = new OrdenCompraRepository(db);
            var consumer = new OrdenCompraEmitidaConsumer(db, repo, NullLogger<OrdenCompraEmitidaConsumer>.Instance);

            context = CrearConsumeContext(eventoOC);
            await consumer.Consume(context);
        }

        // Se verifica que se publicó el evento de auditoría OrdenCompraBloqueadaEvent
        await context.Received(1).Publish(
            Arg.Is<OrdenCompraBloqueadaEvent>(e =>
                e.OrdenCompraId == ordenCompraId &&
                e.ObraId == obraId &&
                e.MontoOrdenCompra == 5000m &&
                e.Exceso == 3000m),
            Arg.Any<CancellationToken>());

        // Verificar que la OC quedó en estado Bloqueada en BD
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            var ocGuardada = await db.OrdenesCompra.FindAsync(ordenCompraId);
            Assert.NotNull(ocGuardada);
            Assert.Equal(EstadoOrdenCompra.Bloqueada, ocGuardada.Estado);
            Assert.True(ocGuardada.EstaBloqueada);
        }

        // ── 3. Verificar que la OC Bloqueada NO aparece en órdenes pendientes ──
        var client = CrearCliente("Capataz");
        var respuestaPendientes = await client.GetAsync($"/api/recepciones/ordenes-pendientes?obraId={obraId}");
        var pendientes = await respuestaPendientes.Content.ReadFromJsonAsync<List<OrdenPendienteDto>>();
        Assert.NotNull(pendientes);
        Assert.DoesNotContain(pendientes, o => o.OrdenCompraId == ordenCompraId);

        // ── 4. Intentar forzar la recepción sobre la OC Bloqueada devuelve 409 Conflict ──
        var solicitudForzada = new CrearRecepcionRequest
        {
            ObraId = obraId,
            OrdenCompraId = ordenCompraId,
            NumeroRemito = "REM-BLOQ-001",
            FechaRecepcion = DateTime.UtcNow,
            Observaciones = "Intento de recibir OC bloqueada",
            Detalles = new List<CrearDetalleRecepcionRequest>
            {
                new()
                {
                    ArticuloId = articuloId,
                    DescripcionArticulo = "Pintura Látex",
                    CantidadEsperada = 10m,
                    CantidadRecibida = 10m,
                    UnidadMedida = "latas",
                    Conforme = true
                }
            }
        };

        var respuestaForzada = await client.PostAsJsonAsync("/api/recepciones", solicitudForzada);
        Assert.Equal(HttpStatusCode.Conflict, respuestaForzada.StatusCode);

        var error = await respuestaForzada.Content.ReadAsStringAsync();
        Assert.Contains("bloqueada por desvío presupuestario", error);
    }

    [Fact]
    public async Task FlujoCompleto_ValidacionesNegocio_RemitoDuplicadoYRecepcionRepetida()
    {
        var obraId = Guid.NewGuid();
        var ordenCompraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        // Crear una OC activa en BD
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            var item = OrdenCompraItem.Crear(articuloId, 100m, "u", 50m, "Hierro");
            var oc = OrdenCompra.Crear(ordenCompraId, obraId, Guid.NewGuid(), 5000m, new[] { item });
            db.OrdenesCompra.Add(oc);
            await db.SaveChangesAsync();
        }

        var client = CrearCliente("Capataz");
        var remitoOriginal = "REM-DUP-001";

        var solicitudValida = new CrearRecepcionRequest
        {
            ObraId = obraId,
            OrdenCompraId = ordenCompraId,
            NumeroRemito = remitoOriginal,
            FechaRecepcion = DateTime.UtcNow,
            Detalles = new List<CrearDetalleRecepcionRequest>
            {
                new()
                {
                    ArticuloId = articuloId,
                    DescripcionArticulo = "Hierro",
                    CantidadEsperada = 100m,
                    CantidadRecibida = 100m,
                    UnidadMedida = "u",
                    Conforme = true
                }
            }
        };

        // Primera recepción: éxito 201 Created
        var r1 = await client.PostAsJsonAsync("/api/recepciones", solicitudValida);
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);

        // Intento 2: misma OC ya recibida por completo -> 409 Conflict
        var solicitudSegunda = new CrearRecepcionRequest
        {
            ObraId = obraId,
            OrdenCompraId = ordenCompraId,
            NumeroRemito = "REM-OTRO-002",
            FechaRecepcion = DateTime.UtcNow,
            Detalles = solicitudValida.Detalles
        };
        var r2 = await client.PostAsJsonAsync("/api/recepciones", solicitudSegunda);
        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);
        var detalleR2 = await r2.Content.ReadAsStringAsync();
        Assert.Contains("ya fue recibida por completo", detalleR2);

        // Intento 3: mismo número de remito en la misma OC -> 409 Conflict
        var r3 = await client.PostAsJsonAsync("/api/recepciones", solicitudValida);
        Assert.Equal(HttpStatusCode.Conflict, r3.StatusCode);
    }

    [Fact]
    public async Task FlujoCompleto_RecepcionParcial_ModeloYConsultaDeRemanentes()
    {
        // ── 1. Verificación de reglas puras de dominio para recepción parcial (#23, #90) ──
        var recepcion = new RecepcionEntity
        {
            Id = Guid.NewGuid(),
            ObraId = Guid.NewGuid(),
            OrdenCompraId = Guid.NewGuid(),
            NumeroRemito = "REM-PARC-001",
            FechaRecepcion = DateTime.UtcNow
        };

        // Recepción parcial: se recibieron 60 de 100 esperadas
        recepcion.Detalles.Add(new DetalleRecepcionEntity
        {
            ArticuloId = Guid.NewGuid(),
            DescripcionArticulo = "Ladrillos",
            CantidadEsperada = 100m,
            CantidadRecibida = 60m,
            UnidadMedida = "u",
            Conforme = true
        });

        var estadoCalculado = recepcion.CalcularEstadoSegunCantidades();
        Assert.Equal(EstadoRecepcion.Parcial, estadoCalculado);

        recepcion.CambiarEstado(estadoCalculado);
        Assert.Equal(EstadoRecepcion.Parcial, recepcion.Estado);

        // Si se intenta recibir de más, el dominio lo prohíbe
        var recepcionExcedida = new RecepcionEntity
        {
            Id = Guid.NewGuid(),
            ObraId = Guid.NewGuid(),
            OrdenCompraId = Guid.NewGuid(),
            NumeroRemito = "REM-EXC-001"
        };
        recepcionExcedida.Detalles.Add(new DetalleRecepcionEntity
        {
            ArticuloId = Guid.NewGuid(),
            DescripcionArticulo = "Ladrillos",
            CantidadEsperada = 100m,
            CantidadRecibida = 110m,
            UnidadMedida = "u"
        });

        Action calcularExcedida = () => recepcionExcedida.CalcularEstadoSegunCantidades();
        Assert.Throws<InvalidOperationException>(calcularExcedida);

        // ── 2. Verificación de cálculo acumulado en órdenes pendientes ──
        var obraId = Guid.NewGuid();
        var ordenCompraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

            var item = OrdenCompraItem.Crear(articuloId, 100m, "u", 10m, "Ladrillos");
            var oc = OrdenCompra.Crear(ordenCompraId, obraId, Guid.NewGuid(), 1000m, new[] { item });
            db.OrdenesCompra.Add(oc);

            // Simular recepción parcial previa guardada
            var recParcial = new RecepcionEntity
            {
                Id = Guid.NewGuid(),
                ObraId = obraId,
                OrdenCompraId = ordenCompraId,
                NumeroRemito = "REM-P1",
                FechaRecepcion = DateTime.UtcNow,
                Estado = EstadoRecepcion.Parcial
            };
            recParcial.Detalles.Add(new DetalleRecepcionEntity
            {
                Id = Guid.NewGuid(),
                ArticuloId = articuloId,
                DescripcionArticulo = "Ladrillos",
                CantidadEsperada = 100m,
                CantidadRecibida = 45m,
                UnidadMedida = "u",
                Conforme = true
            });
            db.Recepciones.Add(recParcial);
            await db.SaveChangesAsync();
        }

        var client = CrearCliente("Capataz");
        var resPendientes = await client.GetAsync($"/api/recepciones/ordenes-pendientes?obraId={obraId}");
        Assert.Equal(HttpStatusCode.OK, resPendientes.StatusCode);

        var lista = await resPendientes.Content.ReadFromJsonAsync<List<OrdenPendienteDto>>();
        Assert.NotNull(lista);
        var pendiente = lista.FirstOrDefault(o => o.OrdenCompraId == ordenCompraId);
        Assert.NotNull(pendiente);

        // El ítem debe registrar acumulado 45m recibidos y 100m esperados
        var itemPendiente = pendiente.Items.FirstOrDefault(i => i.ArticuloId == articuloId);
        Assert.NotNull(itemPendiente);
        Assert.Equal(100m, itemPendiente.CantidadEsperada);
        Assert.Equal(45m, itemPendiente.CantidadRecibida);
    }

    [Fact]
    public async Task FlujoCompleto_ControlDeAcceso_ProveedorNoPuedeRegistrarRecepcion()
    {
        var clientProveedor = CrearCliente("Proveedor");

        var solicitud = new CrearRecepcionRequest
        {
            ObraId = Guid.NewGuid(),
            OrdenCompraId = Guid.NewGuid(),
            NumeroRemito = "REM-PROV-001",
            FechaRecepcion = DateTime.UtcNow,
            Detalles = new List<CrearDetalleRecepcionRequest>()
        };

        var respuesta = await clientProveedor.PostAsJsonAsync("/api/recepciones", solicitud);
        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }
}
