using System;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

public record ObtenerFotoRecepcionQuery(Guid RecepcionId, Guid FotoId);
