using System;
using System.Collections.Generic;
using System.Linq;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Domain.Common;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;
using DetalleRecepcionEntity = ObraSync.Recepcion.Domain.Entities.DetalleRecepcion;
using FotoRecepcionEntity = ObraSync.Recepcion.Domain.Entities.FotoRecepcion;

namespace ObraSync.Recepcion.Application.Mappings;

/// <summary>
/// Métodos de extensión para mapeo de entidades de Recepción a DTOs.
/// Centraliza la proyección de dominio a aplicación y la normalización de fechas UTC.
/// </summary>
public static class RecepcionMappings
{
    public static RecepcionDto ToDto(
        this RecepcionEntity entidad,
        IReadOnlyList<FotoRecepcionDto>? fotosProyectadas = null)
    {
        ArgumentNullException.ThrowIfNull(entidad);

        var fotos = fotosProyectadas is not null
            ? fotosProyectadas.ToList()
            : entidad.Fotos
                .OrderBy(f => f.Orden)
                .ThenBy(f => f.CreatedAt)
                .Select(f => f.ToDto())
                .ToList();

        return new RecepcionDto
        {
            Id = entidad.Id,
            ObraId = entidad.ObraId,
            OrdenCompraId = entidad.OrdenCompraId,
            RecepcionadoPorUsuarioId = entidad.RecepcionadoPorUsuarioId,
            NumeroRemito = entidad.NumeroRemito,
            FechaRecepcion = FechaUtc.Normalizar(entidad.FechaRecepcion),
            Estado = entidad.Estado,
            MotivoProblema = entidad.MotivoProblema,
            Observaciones = entidad.Observaciones,
            CreatedAt = FechaUtc.Normalizar(entidad.CreatedAt),
            UpdatedAt = FechaUtc.Normalizar(entidad.UpdatedAt),
            Detalles = entidad.Detalles.Select(d => d.ToDto()).ToList(),
            Fotos = fotos
        };
    }

    public static FotoRecepcionDto ToDto(this FotoRecepcionEntity entidad)
    {
        ArgumentNullException.ThrowIfNull(entidad);

        return CrearFotoDto(
            entidad.Id,
            entidad.RecepcionId,
            entidad.NombreArchivo,
            entidad.ContentType,
            entidad.TamanoBytes,
            entidad.Orden);
    }

    public static FotoRecepcionDto CrearFotoDto(
        Guid fotoId,
        Guid recepcionId,
        string nombreArchivo,
        string contentType,
        long tamanoBytes,
        int orden)
    {
        return new FotoRecepcionDto
        {
            Id = fotoId,
            NombreArchivo = nombreArchivo,
            ContentType = contentType,
            TamanoBytes = tamanoBytes,
            Orden = orden,
            Url = $"/api/recepciones/{recepcionId}/fotos/{fotoId}"
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
