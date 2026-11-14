using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using ObraSync.Usuarios.Application.DTOs;

namespace ObraSync.Usuarios.Application.Interfaces;

public interface IObraService
{
    Task<CrearObraResponseDto> CrearObraAsync(CrearObraDto dto);
    Task<IEnumerable<ObraResponseDto>> ListarObrasAsync(ClaimsPrincipal? usuario = null);
    Task<ObraResponseDto> EditarObraAsync(Guid id, ActualizarObraDto dto);
    Task DarDeBajaObraAsync(Guid id);
}
