using MediatR;
using ObraSync.Requisiciones.Application.DTOs;

namespace ObraSync.Requisiciones.Application.Commands.CrearInsumo
{
    public record CrearInsumoCommand(
        string Codigo,
        string Descripcion,
        string UnidadMedida,
        bool Activo = true
    ) : IRequest<InsumoCatalogoDto>;
}
