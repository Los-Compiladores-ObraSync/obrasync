using System.ComponentModel.DataAnnotations.Schema;

namespace ObraSync.Licitaciones.Domain.Entities;

public class OrdenCompra
{
    public const int MaxLongitudObservacionDespacho = 200;
    public const int MaxLongitudMotivoProblema = 200;

    public Guid OrdenCompraId { get; set; }
    public Guid LicitacionId { get; set; }
    public Guid CotizacionId { get; set; }
    public Guid ProveedorId { get; set; }
    public string Estado { get; set; } = "Emitida";
    public DateTime FechaEmision { get; set; } = DateTime.UtcNow;
    public DateTime? FechaDespacho { get; set; }
    public int NumeroDespacho { get; set; }
    public Guid? DespachadaPor { get; set; }
    public DateTime? FechaEstimadaLlegada { get; set; }
    public string? Observacion { get; set; }
    public bool TieneRecepcionParcialPendiente { get; set; }
    public DateTime? FechaUltimaRecepcionParcial { get; set; }
    public bool RecibidaCompleta { get; set; }
    public DateTime? FechaRecepcionCompleta { get; set; }
    public bool TieneProblemaAbierto { get; set; }
    public string? MotivoProblemaAbierto { get; set; }
    public DateTime? FechaProblemaAbierto { get; set; }
    public DateTime? ProblemaResueltoEn { get; set; }
    public Guid? ProblemaResueltoPor { get; set; }
    public decimal TotalFlete { get; set; }
    public List<OrdenCompraItem> Items { get; set; } = new();

    [NotMapped]
    public Guid ObraId { get; set; }

    [NotMapped]
    public DateTime? DespachadaEn
    {
        get => FechaDespacho;
        set => FechaDespacho = value;
    }

    [NotMapped]
    public bool FueDespachada =>
        string.Equals(Estado, "Despachada", StringComparison.OrdinalIgnoreCase) || FechaDespacho.HasValue;

    [NotMapped]
    public bool PuedeRedespachar =>
        FueDespachada && !RecibidaCompleta &&
        (TieneRecepcionParcialPendiente ||
         (TieneProblemaAbierto && FechaProblemaAbierto.HasValue &&
          (!FechaDespacho.HasValue || FechaProblemaAbierto.Value >= FechaDespacho.Value)));

    public bool Despachar(
        Guid despachadaPor,
        DateTime? fechaDespachoUtc = null,
        DateTime? fechaEstimadaLlegada = null,
        string? observacion = null)
    {
        if (despachadaPor == Guid.Empty)
        {
            throw new ArgumentException("El identificador del usuario que despacha es obligatorio.", nameof(despachadaPor));
        }

        if (RecibidaCompleta)
        {
            throw new InvalidOperationException(
                "La orden de compra ya fue recibida completa y no admite nuevos despachos.");
        }

        if (FueDespachada && !PuedeRedespachar)
        {
            throw new InvalidOperationException(
                "La orden de compra ya fue despachada y se encuentra en tránsito esperando recepción.");
        }

        var instante = NormalizarAUtc(fechaDespachoUtc ?? DateTime.UtcNow);

        DateTime? fechaEstimadaUtc = null;
        if (fechaEstimadaLlegada.HasValue)
        {
            fechaEstimadaUtc = NormalizarAUtc(fechaEstimadaLlegada.Value);
            if (fechaEstimadaUtc.Value.Date < instante.Date)
            {
                throw new ArgumentException(
                    "La fecha estimada de llegada no puede ser anterior a hoy.",
                    nameof(fechaEstimadaLlegada));
            }
        }

        string? observacionNormalizada = null;
        if (!string.IsNullOrWhiteSpace(observacion))
        {
            observacionNormalizada = observacion.Trim();
            if (observacionNormalizada.Length > MaxLongitudObservacionDespacho)
            {
                throw new ArgumentException(
                    $"La observación no puede superar los {MaxLongitudObservacionDespacho} caracteres.",
                    nameof(observacion));
            }
        }

        var esRedespacho = PuedeRedespachar;

        Estado = "Despachada";
        NumeroDespacho = esRedespacho
            ? (NumeroDespacho > 0 ? NumeroDespacho + 1 : 2)
            : 1;
        FechaDespacho = instante;
        DespachadaPor = despachadaPor;
        FechaEstimadaLlegada = fechaEstimadaUtc;
        Observacion = observacionNormalizada;
        TieneRecepcionParcialPendiente = false;
        ResolverProblema(instante, despachadaPor);
        return true;
    }

    public bool RegistrarRecepcionParcial(DateTime? fechaRecepcionUtc = null)
    {
        if (RecibidaCompleta)
        {
            return false;
        }

        TieneRecepcionParcialPendiente = true;
        FechaUltimaRecepcionParcial = NormalizarAUtc(fechaRecepcionUtc ?? DateTime.UtcNow);
        return true;
    }

    public bool MarcarComoRecibidaCompleta(DateTime? fechaRecepcionUtc = null)
    {
        if (RecibidaCompleta)
        {
            return false;
        }

        FechaRecepcionCompleta = NormalizarAUtc(fechaRecepcionUtc ?? DateTime.UtcNow);
        RecibidaCompleta = true;
        TieneRecepcionParcialPendiente = false;
        return true;
    }

    public void AbrirProblema(string motivo, DateTime? fechaUtc = null)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException("El motivo del problema es obligatorio.", nameof(motivo));
        }

        var motivoNormalizado = motivo.Trim();
        if (motivoNormalizado.Length > MaxLongitudMotivoProblema)
        {
            motivoNormalizado = motivoNormalizado[..MaxLongitudMotivoProblema];
        }

        TieneProblemaAbierto = true;
        MotivoProblemaAbierto = motivoNormalizado;
        FechaProblemaAbierto = NormalizarAUtc(fechaUtc ?? DateTime.UtcNow);
        ProblemaResueltoEn = null;
        ProblemaResueltoPor = null;
    }

    public bool ResolverProblema(DateTime? fechaUtc = null, Guid? resueltoPor = null)
    {
        if (!TieneProblemaAbierto)
        {
            return false;
        }

        TieneProblemaAbierto = false;
        ProblemaResueltoEn = NormalizarAUtc(fechaUtc ?? DateTime.UtcNow);
        ProblemaResueltoPor = resueltoPor is Guid uid && uid != Guid.Empty ? uid : null;
        return true;
    }

    internal static DateTime NormalizarAUtc(DateTime fecha) => fecha.Kind switch
    {
        DateTimeKind.Utc => fecha,
        DateTimeKind.Local => fecha.ToUniversalTime(),
        _ => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
    };
}
