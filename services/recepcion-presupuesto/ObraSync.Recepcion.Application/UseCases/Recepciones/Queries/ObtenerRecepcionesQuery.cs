using System;
using System.Collections.Generic;
using ObraSync.Recepcion.Application.DTOs;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

public record ObtenerRecepcionesQuery(
    Guid? ObraId = null,
    Guid? OrdenCompraId = null,
    IReadOnlyList<Guid>? ObrasPermitidas = null,
    ParametrosPagina? Paginacion = null);
