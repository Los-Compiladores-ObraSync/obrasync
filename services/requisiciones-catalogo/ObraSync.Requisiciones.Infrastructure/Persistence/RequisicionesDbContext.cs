using System.Reflection;
using MassTransit;
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

            // Tablas del outbox de MassTransit: guardan los eventos junto con el cambio de estado
            // (misma transaccion) y un proceso aparte los entrega a RabbitMQ (#134).
            modelBuilder.AddInboxStateEntity();
            modelBuilder.AddOutboxMessageEntity();
            modelBuilder.AddOutboxStateEntity();
        }
    }
}
