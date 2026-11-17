using System;
using ObraSync.Recepcion.Application.DTOs;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

public record ObtenerOrdenesBloqueadasQuery(
    Guid? ObraId = null,
    ParametrosPagina? Paginacion = null);
