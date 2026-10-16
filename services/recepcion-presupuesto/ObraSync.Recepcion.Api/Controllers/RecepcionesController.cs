using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;

namespace ObraSync.Recepcion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RecepcionesController : ControllerBase
{
    private readonly IRecepcionDbContext _context;
    private readonly ILogger<RecepcionesController> _logger;

    public RecepcionesController(IRecepcionDbContext context, ILogger<RecepcionesController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Obtiene las recepciones registradas, opcionalmente filtradas por ObraId (GUID externo de Usuarios y Obras).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ObtenerRecepciones([FromQuery] Guid? obraId, CancellationToken cancellationToken)
    {
        var query = _context.Recepciones
            .Include(r => r.Detalles)
            .AsNoTracking();

        if (obraId.HasValue)
        {
            query = query.Where(r => r.ObraId == obraId.Value);
        }

        var resultado = await query.ToListAsync(cancellationToken);
        return Ok(resultado);
    }
}
