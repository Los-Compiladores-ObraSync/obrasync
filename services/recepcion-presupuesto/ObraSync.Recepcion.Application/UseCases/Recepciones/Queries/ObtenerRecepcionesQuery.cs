using System;
using System.Collections.Generic;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

public record ObtenerRecepcionesQuery(
    Guid? ObraId = null,
    Guid? OrdenCompraId = null,
    IReadOnlyList<Guid>? ObrasPermitidas = null);
