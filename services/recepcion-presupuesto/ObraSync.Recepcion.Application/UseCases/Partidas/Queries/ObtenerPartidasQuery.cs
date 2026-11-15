using System;

namespace ObraSync.Recepcion.Application.UseCases.Partidas.Queries;

public record ObtenerPartidasQuery(Guid? ObraId = null, string? Etapa = null);
