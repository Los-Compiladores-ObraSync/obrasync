namespace ObraSync.Asistente.Application;

// Forma EXACTA de BorradorAprobacionDto en contracts/openapi/asistente-openapi.json.
// Igual que el borrador de requisición: NO aprueba ni rechaza nada. Solo viaja en la respuesta y el frontend llama a
// POST /api/requisiciones/{id}/aprobaciones (o /rechazos, con el motivo; o POST /api/licitaciones para publicar), con el JWT
// del usuario, recién cuando la persona toca Confirmar. El campo Accion dice cuál escritura representa
// (#141 aprobar, #149 rechazar y publicar licitación).

/// <summary>Un material de la requisición que se va a aprobar (para que el Comprador la revise).</summary>
public sealed record AprobacionResumenItem(string Descripcion, decimal Cantidad, string UnidadMedida);

/// <summary>Lo que se muestra en la tarjeta de confirmación.</summary>
public sealed record AprobacionResumen(
    string Numero, string? Obra, string? Etapa, IReadOnlyList<AprobacionResumenItem> Items);

/// <summary>
/// Borrador de aprobar o rechazar UNA requisición Pendiente, o de publicar la licitación de una Aprobada.
/// Su existencia NO significa que se haya hecho.
/// Motivo solo viene al rechazar (es obligatorio en el backend, 1 a 500 caracteres).
/// DiasParaCotizar solo viene al publicar: el frontend calcula la fecha límite en SU hora local (el servidor no sabe en
/// qué zona está la persona) y la manda al confirmar, igual que la pantalla de la requisición.
/// </summary>
public sealed record BorradorAprobacion(
    string Accion, string Titulo, Guid RequisicionId, AprobacionResumen Resumen,
    string? Motivo = null, int? DiasParaCotizar = null)
{
    public const string AccionAprobarRequisicion = "aprobarRequisicion";
    public const string AccionRechazarRequisicion = "rechazarRequisicion";
    public const string AccionPublicarLicitacion = "publicarLicitacion";
}
