using System;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Domain.Common;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Presupuestos;

namespace ObraSync.Recepcion.Application.Mappings;

/// <summary>
/// Métodos de extensión para proyectar una OrdenCompra bloqueada a OrdenBloqueadaDto (#210).
/// </summary>
public static class OrdenCompraMappings
{
    public static OrdenBloqueadaDto ToBloqueadaDto(
        this OrdenCompra ordenCompra,
        PartidaPresupuestaria? partida)
    {
        ArgumentNullException.ThrowIfNull(ordenCompra);

        var evaluacion = EvaluadorDesvioPresupuestario.Evaluar(partida, ordenCompra.MontoTotal);
        var fechaBloqueo = FechaUtc.Normalizar(
            ordenCompra.FechaBloqueo ?? ordenCompra.UpdatedAt ?? ordenCompra.CreatedAt);
        var motivo = !string.IsNullOrWhiteSpace(ordenCompra.MotivoBloqueo)
            ? ordenCompra.MotivoBloqueo!
            : evaluacion.Motivo;

        return new OrdenBloqueadaDto
        {
            OrdenCompraId = ordenCompra.Id,
            ObraId = ordenCompra.ObraId,
            PartidaId = partida?.Id,
            Etapa = ordenCompra.Etapa,
            MontoOrdenCompra = ordenCompra.MontoTotal,
            MontoAsignadoPartida = evaluacion.MontoAsignado,
            MontoComprometidoActual = evaluacion.GastoAcumulado,
            MontoProyectado = evaluacion.MontoProyectado,
            Exceso = evaluacion.Exceso,
            Motivo = motivo,
            FechaBloqueo = fechaBloqueo
        };
    }
}
