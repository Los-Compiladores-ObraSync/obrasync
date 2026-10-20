using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Infrastructure.Persistence
{
    public class RequisicionesDbContext : DbContext
    {
        public RequisicionesDbContext(DbContextOptions<RequisicionesDbContext> options) : base(options)
        {
        }

        public DbSet<Insumo> Insumos => Set<Insumo>();
        public DbSet<Requisicion> Requisiciones => Set<Requisicion>();
        public DbSet<RequisicionItem> RequisicionItems => Set<RequisicionItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }
}
