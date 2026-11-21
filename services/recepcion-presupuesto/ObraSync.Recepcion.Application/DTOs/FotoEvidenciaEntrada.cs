namespace ObraSync.Recepcion.Application.DTOs;

/// <summary>
/// Archivo binario de foto de evidencia recibido desde la capa API para validación y persistencia (#217).
/// </summary>
public record FotoEvidenciaEntrada(
    string NombreArchivo,
    byte[] Contenido);
