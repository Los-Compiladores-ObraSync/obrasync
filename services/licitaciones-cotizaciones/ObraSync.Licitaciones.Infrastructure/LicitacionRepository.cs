using Microsoft.EntityFrameworkCore;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Infrastructure;

public class LicitacionRepository : ILicitacionRepository
{
    private readonly LicitacionesDbContext _db;

    public LicitacionRepository(LicitacionesDbContext db) => _db = db;

    public Task<RequisicionAprobada?> ObtenerRequisicionAsync(Guid requisicionId, CancellationToken ct) =>
        _db.RequisicionesAprobadas.AsNoTracking()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.RequisicionId == requisicionId, ct);

    public Task<bool> ExisteAbiertaAsync(Guid requisicionId, CancellationToken ct) =>
        _db.Licitaciones.AnyAsync(l => l.RequisicionId == requisicionId && l.Estado == EstadoLicitacion.Abierta, ct);

    public async Task AgregarAsync(Licitacion licitacion, CancellationToken ct)
    {
        _db.Licitaciones.Add(licitacion);
        await _db.SaveChangesAsync(ct);
    }
}