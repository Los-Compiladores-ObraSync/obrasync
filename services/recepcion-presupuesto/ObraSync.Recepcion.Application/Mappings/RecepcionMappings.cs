using System;
using System.Linq;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Domain.Common;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;
using DetalleRecepcionEntity = ObraSync.Recepcion.Domain.Entities.DetalleRecepcion;

namespace ObraSync.Recepcion.Application.Mappings;

/// <summary>
/// Métodos de extensión para mapeo de entidades de Recepción a DTOs.
/// Centraliza la proyección de dominio a aplicación y la normalización de fechas UTC.
/// </summary>
public static class RecepcionMappings
{
    public static RecepcionDto ToDto(this RecepcionEntity entidad)
    {
        ArgumentNullException.ThrowIfNull(entidad);

        return new RecepcionDto
        {
            Id = entidad.Id,
            ObraId = entidad.ObraId,
            OrdenCompraId = entidad.OrdenCompraId,
            RecepcionadoPorUsuarioId = entidad.RecepcionadoPorUsuarioId,
            NumeroRemito = entidad.NumeroRemito,
            FechaRecepcion = FechaUtc.Normalizar(entidad.FechaRecepcion),
            Estado = entidad.Estado,
            Observaciones = entidad.Observaciones,
            CreatedAt = FechaUtc.Normalizar(entidad.CreatedAt),
            UpdatedAt = FechaUtc.Normalizar(entidad.UpdatedAt),
            Detalles = entidad.Detalles.Select(d => d.ToDto()).ToList()
        };
    }

    public static DetalleRecepcionDto ToDto(this DetalleRecepcionEntity entidad)
    {
        ArgumentNullException.ThrowIfNull(entidad);

        return new DetalleRecepcionDto
        {
            Id = entidad.Id,
            RecepcionId = entidad.RecepcionId,
            ArticuloId = entidad.ArticuloId,
            DescripcionArticulo = entidad.DescripcionArticulo,
            CantidadEsperada = entidad.CantidadEsperada,
            CantidadRecibida = entidad.CantidadRecibida,
            UnidadMedida = entidad.UnidadMedida,
            Conforme = entidad.Conforme,
            Observacion = entidad.Observacion,
            CreatedAt = FechaUtc.Normalizar(entidad.CreatedAt),
            UpdatedAt = FechaUtc.Normalizar(entidad.UpdatedAt)
        };
    }
}
