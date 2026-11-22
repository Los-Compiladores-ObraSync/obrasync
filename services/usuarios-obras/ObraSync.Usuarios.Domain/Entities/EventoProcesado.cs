namespace ObraSync.Usuarios.Domain.Entities;

public class EventoProcesado
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string TipoEvento { get; set; } = string.Empty;
    public DateTime ProcesadoEn { get; set; } = DateTime.UtcNow;
}
