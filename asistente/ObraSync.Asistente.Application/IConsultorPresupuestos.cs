namespace ObraSync.Asistente.Application;

/// <summary>Una partida presupuestaria devuelta por la API de Recepción para una obra y etapa.</summary>
public sealed record PartidaPresupuestariaAsistente(
    Guid Id,
    Guid ObraId,
    string? Etapa,
    string CodigoPartida,
    string Descripcion,
    decimal MontoAsignado,
    decimal MontoComprometido,
    decimal MontoEjecutado,
    decimal MontoDisponible,
    string Moneda);

/// <summary>Resultado de consultar partidas: lista y total, o un código estable de error.</summary>
public sealed record ResultadoPartidasPresupuestarias(
    IReadOnlyList<PartidaPresupuestariaAsistente>? Partidas,
    int? Total,
    string? Error)
{
    public static ResultadoPartidasPresupuestarias Bien(
        IReadOnlyList<PartidaPresupuestariaAsistente> partidas,
        int? total) => new(partidas, total, null);

    public static ResultadoPartidasPresupuestarias Falla(string codigo) => new(null, null, codigo);
}

/// <summary>
/// Fuente de las partidas presupuestarias. La API conserva la autorización por JWT;
/// la integración de OC bloqueadas queda fuera hasta que exista el contrato #210.
/// </summary>
public interface IConsultorPresupuestos
{
    Task<ResultadoPartidasPresupuestarias> ListarPartidasAsync(
        Guid obraId,
        string? etapa,
        CancellationToken ct);
}
