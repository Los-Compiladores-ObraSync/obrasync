using System;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Application.Mappings;

/// <summary>
/// Métodos de extensión para mapeo de PartidaPresupuestaria a DTO (#93, #94).
/// Centraliza la proyección de dominio a aplicación evitando duplicaciones.
/// </summary>
public static class PartidaPresupuestariaMappings
{
    public static PartidaPresupuestariaDto ToDto(this PartidaPresupuestaria partida)
    {
        ArgumentNullException.ThrowIfNull(partida);

        return new PartidaPresupuestariaDto
        {
            Id = partida.Id,
            ObraId = partida.ObraId,
            Etapa = partida.Etapa,
            CodigoPartida = partida.CodigoPartida,
            Descripcion = partida.Descripcion,
            MontoAsignado = partida.MontoAsignado,
            MontoComprometido = partida.MontoComprometido,
            MontoEjecutado = partida.MontoEjecutado,
            MontoDisponible = partida.MontoDisponible,
            Moneda = partida.Moneda,
            CreatedAt = partida.CreatedAt
        };
    }
}
