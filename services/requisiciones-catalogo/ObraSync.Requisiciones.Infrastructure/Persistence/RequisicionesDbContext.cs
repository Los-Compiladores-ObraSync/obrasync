using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Infrastructure.Persistence
{
    public class RequisicionesDbContext : DbContext
    {
        private static readonly SemaphoreSlim ConcurrencyLock = new(1, 1);
        private readonly List<Func<CancellationToken, Task>> _accionesPostGuardado = new();

        public RequisicionesDbContext(DbContextOptions<RequisicionesDbContext> options) : base(options)
        {
        }

        public DbSet<Insumo> Insumos => Set<Insumo>();
        public DbSet<Requisicion> Requisiciones => Set<Requisicion>();
        public DbSet<RequisicionItem> RequisicionItems => Set<RequisicionItem>();

        public void EncolarAccionPostGuardado(Func<CancellationToken, Task> accion)
        {
            if (accion != null)
            {
                _accionesPostGuardado.Add(accion);
            }
        }

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            if (Database.IsRelational())
            {
                try
                {
                    var affected = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
                    await EjecutarAccionesPostGuardadoAsync(cancellationToken);
                    return affected;
                }
                catch (DbUpdateConcurrencyException)
                {
                    LimpiarOutboxYAccionesPendientes();
                    throw;
                }
            }

            await ConcurrencyLock.WaitAsync(cancellationToken);
            try
            {
                foreach (var entry in ChangeTracker.Entries<Requisicion>().ToList())
                {
                    if (entry.State == EntityState.Added)
                    {
                        if (entry.Entity.RowVersion == null || entry.Entity.RowVersion.Length == 0)
                        {
                            var initialToken = Guid.NewGuid().ToByteArray();
                            entry.Entity.RowVersion = initialToken;
                            entry.Property(r => r.RowVersion).CurrentValue = initialToken;
                        }
                    }
                    else if (entry.State == EntityState.Modified)
                    {
                        var actualEnBase = await Requisiciones
                            .AsNoTracking()
                            .FirstOrDefaultAsync(r => r.Id == entry.Entity.Id, cancellationToken);

                        var originalRowVersion = entry.Property(r => r.RowVersion).OriginalValue ?? Array.Empty<byte>();
                        var originalEstado = entry.Property(r => r.Estado).OriginalValue;

                        if (actualEnBase == null ||
                            !actualEnBase.RowVersion.SequenceEqual(originalRowVersion) ||
                            !string.Equals(actualEnBase.Estado, originalEstado, StringComparison.OrdinalIgnoreCase))
                        {
                            LimpiarOutboxYAccionesPendientes();
                            throw new DbUpdateConcurrencyException(
                                "La requisición cambió, recargá e intentá nuevamente.");
                        }

                        var nuevoToken = Guid.NewGuid().ToByteArray();
                        entry.Entity.RowVersion = nuevoToken;
                        entry.Property(r => r.RowVersion).CurrentValue = nuevoToken;
                    }
                }

                var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
                await EjecutarAccionesPostGuardadoAsync(cancellationToken);
                return result;
            }
            finally
            {
                ConcurrencyLock.Release();
            }
        }

        private async Task EjecutarAccionesPostGuardadoAsync(CancellationToken cancellationToken)
        {
            if (_accionesPostGuardado.Count == 0)
            {
                return;
            }

            var acciones = _accionesPostGuardado.ToList();
            _accionesPostGuardado.Clear();

            foreach (var accion in acciones)
            {
                await accion(cancellationToken);
            }
        }

        private void LimpiarOutboxYAccionesPendientes()
        {
            _accionesPostGuardado.Clear();

            var outboxEntries = ChangeTracker.Entries()
                .Where(e => e.Entity is OutboxMessage or OutboxState)
                .ToList();

            foreach (var outboxEntry in outboxEntries)
            {
                outboxEntry.State = EntityState.Detached;
            }
        }

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
