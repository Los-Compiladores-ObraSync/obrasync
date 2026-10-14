using Microsoft.EntityFrameworkCore;

namespace ObraSync.Licitaciones.Infrastructure;

public class LicitacionesDbContext : DbContext
{
    public LicitacionesDbContext(DbContextOptions<LicitacionesDbContext> options) : base(options) { }
}