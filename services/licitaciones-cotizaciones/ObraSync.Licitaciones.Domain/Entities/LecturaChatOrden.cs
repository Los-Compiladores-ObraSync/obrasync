namespace ObraSync.Licitaciones.Domain.Entities;

public class LecturaChatOrden
{
    public Guid Id { get; set; }
    public Guid OrdenCompraId { get; set; }
    public Guid UsuarioId { get; set; }
    public DateTime UltimaLecturaEn { get; set; } = DateTime.UtcNow;
}
