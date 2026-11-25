using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Infrastructure.Persistence.Repositories;

public class PartidaPresupuestariaRepository : IPartidaPresupuestariaRepository
{
    private readonly RecepcionDbContext _context;

    public PartidaPresupuestariaRepository(RecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<bool> ExisteCodigoPartidaAsync(Guid obraId, string codigoNormalizadoLower, CancellationToken cancellationToken = default)
    {
        return _context.PartidasPresupuestarias
            .AnyAsync(p => p.ObraId == obraId && p.CodigoPartida.ToLower() == codigoNormalizadoLower, cancellationToken);
    }

    public async Task<IReadOnlyList<string?>> ObtenerEtapasPorObraAsync(Guid obraId, CancellationToken cancellationToken = default)
    {
        return await _context.PartidasPresupuestarias
            .Where(p => p.ObraId == obraId)
            .Select(p => p.Etapa)
            .ToListAsync(cancellationToken);
    }

    public Task<List<PartidaPresupuestaria>> ObtenerPartidasPorObraParaActualizarAsync(Guid obraId, CancellationToken cancellationToken = default)
    {
        return _context.PartidasPresupuestarias
            .Where(p => p.ObraId == obraId)
            .ToListAsync(cancellationToken);
    }

    public Task AgregarPartidaAsync(PartidaPresupuestaria partida, CancellationToken cancellationToken = default)
    {
        return _context.PartidasPresupuestarias.AddAsync(partida, cancellationToken).AsTask();
    }
}
