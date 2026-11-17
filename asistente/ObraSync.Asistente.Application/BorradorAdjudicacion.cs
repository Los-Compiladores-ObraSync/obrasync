namespace ObraSync.Asistente.Application;

// Forma EXACTA de BorradorAdjudicacionDto en contracts/openapi/asistente-openapi.json.
// Igual que los demás borradores: NO adjudica nada. Solo viaja en la respuesta y el frontend llama a
// POST /api/licitaciones/{licitacionId}/adjudicar con { cotizacionId }, con el JWT del usuario, recién cuando la persona
// toca Confirmar. Adjudicar es IRREVERSIBLE (emite la Orden de Compra), por eso la tarjeta se muestra reforzada.

/// <summary>Un material del pliego que se va a comprar (para que el Comprador lo revise).</summary>
public sealed record AdjudicacionItem(string Descripcion, decimal Cantidad, string UnidadMedida);

/// <summary>
/// Lo que se muestra en la tarjeta de confirmación. Todas las cifras salen de la respuesta de la API, nunca del modelo:
/// CostoTotal es el precio de los materiales más el flete, tal como lo calcula el backend.
/// </summary>
/// <param name="HayOfertaConMayorPuntaje">
/// true si la oferta elegida NO es la de mayor puntaje y hay otra que sí lo es: la tarjeta se lo avisa a la persona.
/// Elegir otra es válido (la decide el Comprador), pero tiene que saberlo.
/// </param>
public sealed record AdjudicacionResumen(
    string? NumeroRequisicion, string? Obra, string? ProveedorNombre, string ProveedorCodigo,
    decimal PrecioMateriales, decimal Flete, decimal CostoTotal, int? PlazoEntregaDias, decimal PuntajeTotal,
    bool EsGanadora, bool HayOfertaConMayorPuntaje, IReadOnlyList<AdjudicacionItem> Items);

/// <summary>Borrador de adjudicar UNA oferta de UNA licitación Abierta. Su existencia NO significa que se haya hecho.</summary>
public sealed record BorradorAdjudicacion(
    string Accion, string Titulo, Guid LicitacionId, Guid CotizacionId, AdjudicacionResumen Resumen)
{
    public const string AccionAdjudicarCotizacion = "adjudicarCotizacion";
}
