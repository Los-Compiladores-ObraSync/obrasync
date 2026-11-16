using System;
using System.Collections.Generic;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

public record ObtenerOrdenesPendientesQuery(
    Guid? ObraId = null,
    bool? SinPartida = null,
    IReadOnlyList<Guid>? ObrasPermitidas = null);
