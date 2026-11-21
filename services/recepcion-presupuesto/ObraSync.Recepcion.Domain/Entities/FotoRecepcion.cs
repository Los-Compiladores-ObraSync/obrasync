using System.Text.Json.Serialization;
using ObraSync.Recepcion.Domain.Common;

namespace ObraSync.Recepcion.Domain.Entities;

/// <summary>
/// Foto de evidencia adjunta a una recepción con problema (#217), almacenada en DB_Recepcion (varbinary(max)).
/// </summary>
public class FotoRecepcion : BaseEntity
{
    public Guid RecepcionId { get; set; }

    [JsonIgnore]
    public Recepcion Recepcion { get; set; } = null!;

    public string NombreArchivo { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public byte[] Contenido { get; set; } = Array.Empty<byte>();
    public int Orden { get; set; }
}
