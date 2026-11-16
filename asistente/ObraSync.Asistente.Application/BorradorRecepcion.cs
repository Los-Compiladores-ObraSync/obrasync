namespace ObraSync.Asistente.Application;

// Forma EXACTA de BorradorRecepcionDto en contracts/openapi/asistente-openapi.json.
// Igual que los otros borradores: NO registra nada. Solo viaja en la respuesta y el frontend llama a
// POST /api/recepciones, con el JWT del usuario, recién cuando la persona toca Confirmar.

/// <summary>Un material de la recepción, tal como lo espera POST /api/recepciones.</summary>
public sealed record BorradorDetalleRecepcion(
    Guid ArticuloId, string DescripcionArticulo, decimal CantidadEsperada, decimal CantidadRecibida, string UnidadMedida, bool Conforme);

/// <summary>El cuerpo de POST /api/recepciones SIN la fecha: la fecha la pone el frontend al confirmar.</summary>
public sealed record BorradorCrearRecepcion(
    Guid ObraId, Guid OrdenCompraId, string NumeroRemito, string? Observaciones, IReadOnlyList<BorradorDetalleRecepcion> Detalles);

/// <summary>Una línea de lo que se muestra para revisar antes de confirmar.</summary>
public sealed record RecepcionResumenItem(string Descripcion, decimal Cantidad, string UnidadMedida);

/// <summary>Lo que se muestra en la tarjeta de confirmación.</summary>
public sealed record RecepcionResumen(string? Obra, string? Etapa, string NumeroRemito, IReadOnlyList<RecepcionResumenItem> Items);

/// <summary>Borrador de una recepción TOTAL. Su existencia NO significa que se haya registrado nada.</summary>
public sealed record BorradorRecepcion(string Accion, string Titulo, RecepcionResumen Resumen, BorradorCrearRecepcion Recepcion)
{
    public const string AccionRegistrarRecepcion = "registrarRecepcion";
}
