using System;
using System.Collections.Generic;

namespace ObraSync.Requisiciones.Application.Exceptions;

public class ArticuloCatalogoNoEncontradoException : KeyNotFoundException
{
    public Guid ArticuloId { get; }

    public ArticuloCatalogoNoEncontradoException(Guid articuloId)
        : base($"No se encontró el artículo con Id '{articuloId}' en el catálogo.")
    {
        ArticuloId = articuloId;
    }
}
