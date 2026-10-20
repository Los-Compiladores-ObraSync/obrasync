namespace ObraSync.Licitaciones.Infrastructure;

public class EventoProcesado
{
    public int Id { get; set; }
    public Guid EventId { get; set; }
    public string TipoEvento { get; set; } = string.Empty;
    public DateTime ProcesadoEn { get; set; }
}