using System;
using ObraSync.Recepcion.Domain.Enums;

namespace ObraSync.Recepcion.Application.DTOs;

public record OrdenCompraLiberadaDto(
    Guid OrdenCompraId,
    Guid ObraId,
    EstadoOrdenCompra Estado,
    Guid LiberadaPorUsuarioId,
    DateTime LiberadaEn,
    string? MotivoLiberacion,
    decimal MontoTotal,
    Guid? PartidaPresupuestariaId,
    decimal? MontoComprometidoPartida);
