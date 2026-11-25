using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Queries;

public record GetOrdenesCompraQuery(
    Guid? ObraId = null,
    Guid? LicitacionId = null,
    Guid? ProveedorId = null,
    string? Estado = null,
    string? RolUsuario = null,
    Guid? UsuarioId = null,
    ParametrosPagina? Paginacion = null) : IRequest<PaginaDe<OrdenCompraResponseDto>>;
