using System;
using ObraSync.Recepcion.Application.DTOs;

namespace ObraSync.Recepcion.Application.UseCases.Partidas.Queries;

public record ObtenerPartidasQuery(
    Guid? ObraId = null,
    string? Etapa = null,
    ParametrosPagina? Paginacion = null);
