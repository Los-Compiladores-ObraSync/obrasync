namespace ObraSync.Asistente.Application;

/// <summary>Un material de una orden de compra que está esperando recepción.</summary>
public sealed record ItemOrdenPendiente(Guid ArticuloId, string Descripcion, decimal CantidadEsperada, string UnidadMedida);

/// <summary>Una orden de compra despachada que todavía no se recibió por completo.</summary>
public sealed record OrdenPendiente(Guid OrdenCompraId, Guid ObraId, string? Etapa, IReadOnlyList<ItemOrdenPendiente> Items);

/// <summary>Resultado de listar las órdenes pendientes: las órdenes y cuántas hay en total, o un error estable.</summary>
public sealed record ResultadoOrdenesPendientes(IReadOnlyList<OrdenPendiente>? Ordenes, int? Total, string? Error)
{
    public static ResultadoOrdenesPendientes Bien(IReadOnlyList<OrdenPendiente> ordenes, int? total) => new(ordenes, total, null);
    public static ResultadoOrdenesPendientes Falla(string codigo) => new(null, null, codigo);
}

/// <summary>
/// De dónde salen las órdenes pendientes de recibir (#147). La API ya filtra por rol y solo devuelve las despachadas (#204).
/// Solo lee: registrar la recepción lo hace el frontend, con el JWT del usuario, cuando toca Confirmar.
/// </summary>
public interface IConsultorRecepciones
{
    /// <summary>Lista las órdenes pendientes de recepción (hasta "maximo").</summary>
    Task<ResultadoOrdenesPendientes> ListarPendientesAsync(int maximo, CancellationToken ct);
}
