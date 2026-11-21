using System;

namespace ObraSync.Recepcion.Application.DTOs;

/// <summary>
/// Metadatos de una foto de evidencia asociada a una recepción con problema (#217).
/// </summary>
public class FotoRecepcionDto
{
    public Guid Id { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public int Orden { get; set; }
    public string Url { get; set; } = string.Empty;
}
