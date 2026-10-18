using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Application.Common.Interfaces;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Infrastructure.Persistence
{
    public class RequisicionesDbContext : DbContext, IRequisicionesDbContext
    {
        public RequisicionesDbContext(DbContextOptions<RequisicionesDbContext> options) : base(options)
        {
        }

        public DbSet<Insumo> Insumos => Set<Insumo>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }
}
