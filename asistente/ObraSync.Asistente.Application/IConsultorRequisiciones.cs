namespace ObraSync.Asistente.Application;

/// <summary>Una requisición en la lista: lo mínimo para que el modelo la identifique y la cuente.</summary>
public sealed record RequisicionResumen(
    Guid Id, string Numero, string Estado, Guid ObraId, string? Etapa, DateTime Fecha, int CantidadItems);

/// <summary>Un material pedido dentro de una requisición.</summary>
public sealed record ItemRequisicion(string Descripcion, decimal Cantidad, string UnidadMedida);

/// <summary>El detalle de una requisición, con sus materiales.</summary>
public sealed record RequisicionDetalle(
    RequisicionResumen Resumen, string? Observaciones, string? MotivoRechazo, IReadOnlyList<ItemRequisicion> Items);

/// <summary>Resultado de listar: las requisiciones (las más nuevas primero) y cuántas hay en total, o un error estable.</summary>
public sealed record ResultadoListaRequisiciones(IReadOnlyList<RequisicionResumen>? Requisiciones, int? Total, string? Error)
{
    public static ResultadoListaRequisiciones Bien(IReadOnlyList<RequisicionResumen> lista, int? total) => new(lista, total, null);
    public static ResultadoListaRequisiciones Falla(string codigo) => new(null, null, codigo);
}

/// <summary>Resultado de pedir una requisición: o está, o hay un código de error estable.</summary>
public sealed record ResultadoRequisicion(RequisicionDetalle? Requisicion, string? Error)
{
    public static ResultadoRequisicion Bien(RequisicionDetalle detalle) => new(detalle, null);
    public static ResultadoRequisicion Falla(string codigo) => new(null, codigo);
}

/// <summary>
/// De dónde salen las requisiciones. La API ya filtra por rol, así el asistente nunca muestra
/// algo que el usuario no podría ver en la aplicación. Solo lee: no hay forma de escribir desde acá.
/// </summary>
public interface IConsultorRequisiciones
{
    /// <summary>Lista las requisiciones (más nuevas primero). "estado" es Pendiente, Aprobada o Rechazada, o null para todas.</summary>
    Task<ResultadoListaRequisiciones> ListarAsync(string? estado, int maximo, CancellationToken ct);

    Task<ResultadoRequisicion> ObtenerAsync(Guid id, CancellationToken ct);
}
