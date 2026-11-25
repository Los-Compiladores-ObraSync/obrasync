using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ObraSync.Licitaciones.Domain.Entities;

public class MagicLink
{
    public Guid Id { get; set; }
    public string Hash { get; set; } = string.Empty;

    [NotMapped]
    public string TokenHash
    {
        get => Hash;
        set => Hash = value;
    }

    public Guid LicitacionId { get; set; }
    public Guid ProveedorId { get; set; }
    /// <summary>
    /// Nombre del proveedor que escribió quien generó el enlace (#206). El Magic Link no tiene JWT, así que es la única fuente
    /// del nombre cuando el proveedor cotiza por este camino. Opcional.
    /// </summary>
    public string? ProveedorNombre { get; set; }
    public DateTime FechaDeVencimiento { get; set; }
    public DateTime? FechaDeUso { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
