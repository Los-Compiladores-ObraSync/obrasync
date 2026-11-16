namespace ObraSync.Asistente.Application;

// Forma EXACTA de BorradorRequisicionDto en contracts/openapi/asistente-openapi.json.
// El borrador NO se guarda en ningún lado: solo viaja en la respuesta, y el frontend crea
// la requisición (con el JWT del usuario) recién cuando la persona toca Confirmar.

/// <summary>Un ítem tal como lo espera POST /api/requisiciones.</summary>
public sealed record BorradorItem(Guid ArticuloId, double Cantidad);

/// <summary>El cuerpo que el frontend envía tal cual a POST /api/requisiciones.</summary>
public sealed record BorradorCrearRequisicion(
    Guid ObraId, string? Etapa, string? Observaciones, IReadOnlyList<BorradorItem> Items);

/// <summary>Una línea de lo que se muestra en pantalla para revisar antes de confirmar.</summary>
public sealed record BorradorResumenItem(string Descripcion, double Cantidad, string UnidadMedida);

/// <summary>Lo que se muestra en la tarjeta de confirmación.</summary>
public sealed record BorradorResumen(string Obra, string? Etapa, IReadOnlyList<BorradorResumenItem> Items);

/// <summary>Borrador de una requisición. Su existencia NO significa que se haya creado nada.</summary>
public sealed record BorradorRequisicion(
    string Accion, string Titulo, BorradorResumen Resumen, BorradorCrearRequisicion Requisicion)
{
    public const string AccionCrearRequisicion = "crearRequisicion";
}
