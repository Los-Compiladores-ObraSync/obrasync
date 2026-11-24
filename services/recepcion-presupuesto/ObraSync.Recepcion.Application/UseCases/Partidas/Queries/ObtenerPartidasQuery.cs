using System;
using System.Collections.Generic;
using ObraSync.Recepcion.Application.DTOs;

namespace ObraSync.Recepcion.Application.UseCases.Partidas.Queries;

public record ObtenerPartidasQuery(
    Guid? ObraId = null,
    string? Etapa = null,
    ParametrosPagina? Paginacion = null,
    // Si viene, solo se devuelven partidas de esas obras (las que tiene asignadas el Capataz que consulta).
    IReadOnlyList<Guid>? ObrasPermitidas = null);
