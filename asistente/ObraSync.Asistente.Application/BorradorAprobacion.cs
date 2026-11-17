namespace ObraSync.Asistente.Application;

// Forma EXACTA de BorradorAprobacionDto en contracts/openapi/asistente-openapi.json.
// Igual que el borrador de requisición: NO aprueba ni rechaza nada. Solo viaja en la respuesta y el frontend llama a
// POST /api/requisiciones/{id}/aprobar (o /rechazar, con el motivo), con el JWT del usuario, recién cuando la persona
// toca Confirmar. El campo Accion dice cuál de las dos escrituras representa (#141 aprobar, #149 rechazar).

/// <summary>Un material de la requisición que se va a aprobar (para que el Comprador la revise).</summary>
public sealed record AprobacionResumenItem(string Descripcion, decimal Cantidad, string UnidadMedida);

/// <summary>Lo que se muestra en la tarjeta de confirmación.</summary>
public sealed record AprobacionResumen(
    string Numero, string? Obra, string? Etapa, IReadOnlyList<AprobacionResumenItem> Items);

/// <summary>
/// Borrador de aprobar o rechazar UNA requisición Pendiente. Su existencia NO significa que se haya hecho.
/// Motivo solo viene cuando la acción es rechazar (es obligatorio en el backend, 1 a 500 caracteres).
/// </summary>
public sealed record BorradorAprobacion(
    string Accion, string Titulo, Guid RequisicionId, AprobacionResumen Resumen, string? Motivo = null)
{
    public const string AccionAprobarRequisicion = "aprobarRequisicion";
    public const string AccionRechazarRequisicion = "rechazarRequisicion";
}
