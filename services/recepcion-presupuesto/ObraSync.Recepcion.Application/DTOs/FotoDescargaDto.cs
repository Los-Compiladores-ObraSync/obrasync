using System;

namespace ObraSync.Recepcion.Application.DTOs;

/// <summary>
/// Datos internos para la descarga autenticada de una foto de evidencia (#217),
/// incluyendo ObraId y ProveedorId para que el controlador verifique permisos por rol.
/// </summary>
public record FotoDescargaDto(
    Guid FotoId,
    Guid RecepcionId,
    Guid ObraId,
    Guid OrdenCompraId,
    Guid? ProveedorId,
    string NombreArchivo,
    string ContentType,
    byte[] Contenido);
