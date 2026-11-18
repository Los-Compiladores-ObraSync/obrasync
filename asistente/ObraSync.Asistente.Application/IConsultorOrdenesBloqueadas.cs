namespace ObraSync.Asistente.Application;

/// <summary>Datos oficiales de una orden bloqueada devueltos por Recepción (#210).</summary>
public sealed record OrdenCompraBloqueadaAsistente(
    Guid OrdenCompraId,
    Guid ObraId,
    string? Etapa,
    decimal MontoOrdenCompra,
    decimal MontoAsignadoPartida,
    decimal MontoComprometidoActual,
    decimal MontoProyectado,
    decimal Exceso,
    string Motivo,
    Guid? PartidaId,
    DateTime FechaBloqueo);

public sealed record ResultadoOrdenesBloqueadas(
    IReadOnlyList<OrdenCompraBloqueadaAsistente>? Ordenes,
    int? Total,
    string? Error)
{
    public static ResultadoOrdenesBloqueadas Bien(
        IReadOnlyList<OrdenCompraBloqueadaAsistente> ordenes,
        int total) => new(ordenes, total, null);

    public static ResultadoOrdenesBloqueadas Falla(string codigo) => new(null, null, codigo);
}

/// <summary>Consulta ordenes bloqueadas usando el JWT del usuario, sujeto a permisos de Recepción.</summary>
public interface IConsultorOrdenesBloqueadas
{
    Task<ResultadoOrdenesBloqueadas> ListarAsync(
        Guid obraId,
        int pagina,
        int tamano,
        CancellationToken ct);
}
