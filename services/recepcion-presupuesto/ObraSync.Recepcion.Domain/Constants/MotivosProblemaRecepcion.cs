namespace ObraSync.Recepcion.Domain.Constants;

/// <summary>
/// Motivos tipificados y obligatorios para registrar una entrega con problema en estado Rechazada (#217).
/// </summary>
public static class MotivosProblemaRecepcion
{
    public const string NoLlegoNada = "No llegó nada";
    public const string LlegoIncompleta = "Llegó incompleta";
    public const string LlegoDanada = "Llegó dañada";
    public const string NoEsLoPedido = "No es lo pedido";

    public static readonly IReadOnlyList<string> Permitidos = new[]
    {
        NoLlegoNada,
        LlegoIncompleta,
        LlegoDanada,
        NoEsLoPedido
    };

    /// <summary>
    /// Devuelve el motivo canónico si coincide (ignorando mayúsculas/minúsculas y espacios extremos);
    /// devuelve null si no pertenece a la lista permitida.
    /// </summary>
    public static string? Normalizar(string? motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            return null;
        }

        var limpio = motivo.Trim();
        return Permitidos.FirstOrDefault(m =>
            string.Equals(m, limpio, StringComparison.OrdinalIgnoreCase));
    }

    public static bool EsValido(string? motivo) => Normalizar(motivo) is not null;
}
