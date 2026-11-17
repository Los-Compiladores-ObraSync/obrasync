namespace ObraSync.Asistente.Application;

/// <summary>Una licitación (pliego) en la lista: lo mínimo para que el modelo la identifique y la cuente.</summary>
public sealed record LicitacionResumen(
    Guid Id, Guid RequisicionId, string? NumeroRequisicion, Guid ObraId, string Estado,
    DateTime FechaLimiteCotizacion, int CantidadItems);

/// <summary>Un material que se pidió cotizar en el pliego.</summary>
public sealed record ItemPliego(string Descripcion, decimal Cantidad, string UnidadMedida);

/// <summary>El detalle de una licitación, con los materiales del pliego.</summary>
public sealed record LicitacionDetalle(LicitacionResumen Resumen, IReadOnlyList<ItemPliego> Items);

/// <summary>
/// Una oferta de un proveedor para una licitación. Todas las cifras las calcula el backend (scoring, costo total):
/// el asistente las copia, nunca las recalcula. ProveedorNombre y Calificacion pueden venir null (todavía no los
/// devuelve Licitaciones, ver #206): en ese caso el asistente lo dice y no los inventa.
/// </summary>
public sealed record CotizacionResumen(
    Guid Id, Guid ProveedorId, string? ProveedorNombre, decimal SubtotalItems, decimal Flete, decimal CostoTotal,
    int? PlazoEntregaDias, decimal PuntajeTotal, bool EsGanadora, bool EsMasBarata, decimal? Calificacion);

/// <summary>Resultado de listar licitaciones: las licitaciones (como las devuelva la API) y cuántas hay en total, o un error estable.</summary>
public sealed record ResultadoListaLicitaciones(IReadOnlyList<LicitacionResumen>? Licitaciones, int? Total, string? Error)
{
    public static ResultadoListaLicitaciones Bien(IReadOnlyList<LicitacionResumen> lista, int? total) => new(lista, total, null);
    public static ResultadoListaLicitaciones Falla(string codigo) => new(null, null, codigo);
}

/// <summary>Resultado de pedir una licitación: o está, o hay un código de error estable.</summary>
public sealed record ResultadoLicitacion(LicitacionDetalle? Licitacion, string? Error)
{
    public static ResultadoLicitacion Bien(LicitacionDetalle detalle) => new(detalle, null);
    public static ResultadoLicitacion Falla(string codigo) => new(null, codigo);
}

/// <summary>Resultado de listar las cotizaciones de una licitación: las ofertas y cuántas hay en total, o un error estable.</summary>
public sealed record ResultadoCotizaciones(IReadOnlyList<CotizacionResumen>? Cotizaciones, int? Total, string? Error)
{
    public static ResultadoCotizaciones Bien(IReadOnlyList<CotizacionResumen> lista, int? total) => new(lista, total, null);
    public static ResultadoCotizaciones Falla(string codigo) => new(null, null, codigo);
}

/// <summary>
/// De dónde salen las licitaciones y sus cotizaciones (#148). La API ya filtra por rol (solo Comprador y Admin
/// pueden ver las cotizaciones), así el asistente nunca muestra algo que el usuario no podría ver en la aplicación.
/// Solo lee: no hay forma de escribir desde acá.
/// </summary>
public interface IConsultorLicitaciones
{
    /// <summary>Lista las licitaciones. "estado" es Abierta, Cerrada o Adjudicada, o null para todas.</summary>
    Task<ResultadoListaLicitaciones> ListarAsync(string? estado, int maximo, CancellationToken ct);

    Task<ResultadoLicitacion> ObtenerAsync(Guid id, CancellationToken ct);

    /// <summary>Las ofertas de una licitación, tal como las devuelve la API (con el puntaje que calcula el backend).</summary>
    Task<ResultadoCotizaciones> ListarCotizacionesAsync(Guid licitacionId, int maximo, CancellationToken ct);
}
