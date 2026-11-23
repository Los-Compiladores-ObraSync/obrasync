using System;
using System.Collections.Generic;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Domain.Entities;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Application;

public class MappingsTests
{
    [Fact]
    public void Insumo_ToDto_Y_CrearInsumoRequest_ToCommand_MapeanTodasLasPropiedades()
    {
        var insumo = new Insumo(
            Guid.NewGuid(),
            "MAT-001",
            "Cemento Portland",
            UnidadesMedida.Bolsa,
            activo: true,
            precioReferencia: 8500m,
            moneda: Monedas.PesoArgentino);

        var dto = insumo.ToDto();
        Assert.Equal(insumo.Id, dto.ArticuloId);
        Assert.Equal("MAT-001", dto.Codigo);
        Assert.Equal("Cemento Portland", dto.Descripcion);
        Assert.Equal(UnidadesMedida.Bolsa, dto.UnidadMedida);
        Assert.Equal(Monedas.PesoArgentino, dto.Moneda);
        Assert.Equal(8500m, dto.PrecioReferencia);
        Assert.True(dto.Activo);

        var request = new CrearInsumoRequestDto
        {
            Codigo = "MAT-002",
            Descripcion = "Arena Gruesa",
            UnidadMedida = UnidadesMedida.MetroCubico,
            Activo = false,
            PrecioReferencia = 28000m,
            Moneda = Monedas.PesoArgentino
        };

        var command = request.ToCommand();
        Assert.Equal("MAT-002", command.Codigo);
        Assert.Equal("Arena Gruesa", command.Descripcion);
        Assert.Equal(UnidadesMedida.MetroCubico, command.UnidadMedida);
        Assert.False(command.Activo);
        Assert.Equal(28000m, command.PrecioReferencia);
        Assert.Equal(Monedas.PesoArgentino, command.Moneda);
    }

    [Fact]
    public void Requisicion_ToDto_Y_ToAprobadaEvent_MapeanItemsEHistorialOrdenado()
    {
        var reqId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var solicitanteId = Guid.NewGuid();
        var aprobadorId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var item = new RequisicionItem(Guid.NewGuid(), reqId, articuloId, "Cemento Portland", 25m, UnidadesMedida.Bolsa);
        var requisicion = new Requisicion(reqId, "REQ-2026-0100", obraId, solicitanteId, new[] { item }, "Estructura", "Obs");

        requisicion.Rechazar(Guid.NewGuid(), "Motivo 2", new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc));
        requisicion.HistorialRechazos.Add(new HistorialRechazoRequisicion(
            Guid.NewGuid(),
            reqId,
            Guid.NewGuid(),
            "Motivo 1",
            new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc)));

        var dto = requisicion.ToDto();
        Assert.Equal(reqId, dto.RequisicionId);
        Assert.Equal("REQ-2026-0100", dto.NumeroRequisicion);
        Assert.Single(dto.Items);
        Assert.Equal(articuloId, dto.Items[0].ArticuloId);
        Assert.Equal(2, dto.HistorialRechazos.Count);
        Assert.Equal("Motivo 1", dto.HistorialRechazos[0].Motivo);
        Assert.Equal("Motivo 2", dto.HistorialRechazos[1].Motivo);

        var evento = requisicion.ToAprobadaEvent(correlationId, aprobadorId);
        Assert.NotEqual(Guid.Empty, evento.EventId);
        Assert.Equal(correlationId, evento.CorrelationId);
        Assert.Equal(reqId, evento.RequisicionId);
        Assert.Equal(aprobadorId, evento.AprobadorId);
        Assert.Equal("Estructura", evento.Etapa);
        Assert.Single(evento.Items);
        Assert.Equal(articuloId, evento.Items[0].ArticuloId);
    }

    [Fact]
    public void RequestRequisicion_ToCreateYToReenviarCommand_MapeanCorrectamente()
    {
        var solicitanteId = Guid.NewGuid();
        var reqId = Guid.NewGuid();
        var items = new List<CrearRequisicionItemDto>
        {
            new() { ArticuloId = Guid.NewGuid(), Cantidad = 10m }
        };

        var createReq = new CrearRequisicionRequestDto
        {
            ObraId = Guid.NewGuid(),
            Etapa = "Fundaciones",
            Observaciones = "Urgente",
            Items = items
        };

        var createCmd = createReq.ToCreateCommand(solicitanteId, "idem-123");
        Assert.Equal(createReq.ObraId, createCmd.ObraId);
        Assert.Equal(solicitanteId, createCmd.SolicitanteId);
        Assert.Equal("idem-123", createCmd.IdempotencyKey);
        Assert.Single(createCmd.Items);

        var editReq = new EditarRequisicionRequestDto
        {
            Etapa = "Estructura",
            Observaciones = "Corregida",
            Items = items
        };

        var reenviarCmd = editReq.ToReenviarCommand(reqId, solicitanteId, esAdmin: true);
        Assert.Equal(reqId, reenviarCmd.RequisicionId);
        Assert.Equal(solicitanteId, reenviarCmd.UsuarioId);
        Assert.True(reenviarCmd.EsAdmin);
        Assert.Equal("Estructura", reenviarCmd.Etapa);

        var rechazarReq = new RechazarRequisicionRequestDto { Motivo = "Excede presupuesto" };
        var rechazarCmd = rechazarReq.ToRechazarCommand(reqId, solicitanteId);
        Assert.Equal(reqId, rechazarCmd.RequisicionId);
        Assert.Equal(solicitanteId, rechazarCmd.RechazadorId);
        Assert.Equal("Excede presupuesto", rechazarCmd.Motivo);

        var rechazarSinMotivoCmd = new RechazarRequisicionRequestDto { Motivo = null }.ToRechazarCommand(reqId, solicitanteId);
        Assert.Equal(string.Empty, rechazarSinMotivoCmd.Motivo);
    }

    [Fact]
    public void Mappings_ConArgumentoNull_LanzanArgumentNullException()
    {
        Insumo? insumo = null;
        CrearInsumoRequestDto? crearInsumoReq = null;
        Requisicion? requisicion = null;
        RequisicionItem? item = null;
        HistorialRechazoRequisicion? historial = null;
        CrearRequisicionRequestDto? crearReq = null;
        EditarRequisicionRequestDto? editReq = null;
        RechazarRequisicionRequestDto? rechazarReq = null;

        Assert.Throws<ArgumentNullException>(() => insumo!.ToDto());
        Assert.Throws<ArgumentNullException>(() => crearInsumoReq!.ToCommand());
        Assert.Throws<ArgumentNullException>(() => requisicion!.ToDto());
        Assert.Throws<ArgumentNullException>(() => requisicion!.ToAprobadaEvent(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<ArgumentNullException>(() => item!.ToDto());
        Assert.Throws<ArgumentNullException>(() => item!.ToAprobadaItemEvent());
        Assert.Throws<ArgumentNullException>(() => historial!.ToDto());
        Assert.Throws<ArgumentNullException>(() => crearReq!.ToCreateCommand(Guid.NewGuid()));
        Assert.Throws<ArgumentNullException>(() => editReq!.ToReenviarCommand(Guid.NewGuid(), Guid.NewGuid(), false));
        Assert.Throws<ArgumentNullException>(() => rechazarReq!.ToRechazarCommand(Guid.NewGuid(), Guid.NewGuid()));
    }
}
