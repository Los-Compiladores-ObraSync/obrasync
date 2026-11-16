using System;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

public record ObtenerOrdenesPendientesQuery(Guid? ObraId = null, bool? SinPartida = null);
