namespace ObraSync.Asistente.Application;

// Forma EXACTA de BorradorAprobacionDto en contracts/openapi/asistente-openapi.json.
// Igual que el borrador de requisición: NO aprueba nada. Solo viaja en la respuesta y el frontend llama a
// POST /api/requisiciones/{id}/aprobar, con el JWT del usuario, recién cuando la persona toca Confirmar.

/// <summary>Un material de la requisición que se va a aprobar (para que el Comprador la revise).</summary>
public sealed record AprobacionResumenItem(string Descripcion, decimal Cantidad, string UnidadMedida);

/// <summary>Lo que se muestra en la tarjeta de confirmación.</summary>
public sealed record AprobacionResumen(
    string Numero, string? Obra, string? Etapa, IReadOnlyList<AprobacionResumenItem> Items);

/// <summary>Borrador de la aprobación de UNA requisición Pendiente. Su existencia NO significa que se haya aprobado.</summary>
public sealed record BorradorAprobacion(string Accion, string Titulo, Guid RequisicionId, AprobacionResumen Resumen)
{
    public const string AccionAprobarRequisicion = "aprobarRequisicion";
}
