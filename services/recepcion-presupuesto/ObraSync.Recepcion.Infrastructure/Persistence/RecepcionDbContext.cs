using MassTransit;
using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Partidas.Queries;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Persistence.Queries;
using ObraSync.Recepcion.Infrastructure.Persistence.Repositories;
using System.Reflection;

namespace ObraSync.Recepcion.Infrastructure.Persistence;

public class RecepcionDbContext :
    DbContext,
    IRecepcionPersistenceContext,
    IRecepcionQueries,
    IPartidaQueries
{
    private readonly OrdenCompraRepository _ordenCompraRepo;
    private readonly RecepcionRepository _recepcionRepo;
    private readonly PartidaPresupuestariaRepository _partidaRepo;
    private readonly RecepcionQueries _recepcionQueries;
    private readonly PartidaQueries _partidaQueries;

    public RecepcionDbContext(DbContextOptions<RecepcionDbContext> options) : base(options)
    {
        _ordenCompraRepo = new OrdenCompraRepository(this);
        _recepcionRepo = new RecepcionRepository(this);
        _partidaRepo = new PartidaPresupuestariaRepository(this);
        _recepcionQueries = new RecepcionQueries(this);
        _partidaQueries = new PartidaQueries(this);
    }

    public DbSet<Domain.Entities.Recepcion> Recepciones => Set<Domain.Entities.Recepcion>();
    public DbSet<DetalleRecepcion> DetallesRecepcion => Set<DetalleRecepcion>();
    public DbSet<FotoRecepcion> FotosRecepcion => Set<FotoRecepcion>();
    public DbSet<PartidaPresupuestaria> PartidasPresupuestarias => Set<PartidaPresupuestaria>();
    public DbSet<OrdenCompra> OrdenesCompra => Set<OrdenCompra>();
    public DbSet<OrdenCompraItem> OrdenesCompraItems => Set<OrdenCompraItem>();

    // Exclusivo de la clase concreta para idempotencia en consumidores de Infrastructure
    public DbSet<EventoProcesado> EventosProcesados => Set<EventoProcesado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tablas del Outbox transaccional de MassTransit (#171)
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        // Aplica automáticamente todas las configuraciones IEntityTypeConfiguration del ensamblado actual
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // En proveedores no relacionales (InMemory en tests), simulamos la atomicidad transaccional
        // y la actualización automática de la columna rowversion de SQL Server (#195).
        if (!Database.IsRelational())
        {
            foreach (var entry in ChangeTracker.Entries<PartidaPresupuestaria>())
            {
                if (entry.State == EntityState.Modified)
                {
                    var originalRowVersion = entry.Property(p => p.RowVersion).OriginalValue;
                    var actualEnStore = await PartidasPresupuestarias
                        .AsNoTracking()
                        .Where(p => p.Id == entry.Entity.Id)
                        .Select(p => p.RowVersion)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (actualEnStore is not null && !actualEnStore.SequenceEqual(originalRowVersion))
                    {
                        throw new DbUpdateConcurrencyException(
                            "Conflicto de concurrencia optimista en PartidaPresupuestaria.");
                    }

                    entry.Property(p => p.RowVersion).CurrentValue = Guid.NewGuid().ToByteArray();
                }
                else if (entry.State == EntityState.Added)
                {
                    entry.Property(p => p.RowVersion).CurrentValue = Guid.NewGuid().ToByteArray();
                }
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        UnitOfWork.GuardarConTraduccionAsync(() => SaveChangesAsync(cancellationToken));

    Task<OrdenCompra?> IOrdenCompraRepository.ObtenerPorIdAsync(Guid ordenCompraId, CancellationToken cancellationToken) =>
        _ordenCompraRepo.ObtenerPorIdAsync(ordenCompraId, cancellationToken);

    Task<OrdenCompra?> IOrdenCompraRepository.ObtenerParaActualizarAsync(Guid ordenCompraId, CancellationToken cancellationToken) =>
        _ordenCompraRepo.ObtenerParaActualizarAsync(ordenCompraId, cancellationToken);

    Task<OrdenCompra?> IOrdenCompraRepository.ObtenerConItemsParaActualizarAsync(Guid ordenCompraId, CancellationToken cancellationToken) =>
        _ordenCompraRepo.ObtenerConItemsParaActualizarAsync(ordenCompraId, cancellationToken);

    Task<bool> IOrdenCompraRepository.ExisteAsync(Guid ordenCompraId, CancellationToken cancellationToken) =>
        _ordenCompraRepo.ExisteAsync(ordenCompraId, cancellationToken);

    void IOrdenCompraRepository.Agregar(OrdenCompra ordenCompra) =>
        _ordenCompraRepo.Agregar(ordenCompra);

    Task<(EstadoRecepcion Estado, DateTime CreatedAt)?> IRecepcionRepository.ObtenerUltimaRecepcionResumenAsync(Guid ordenCompraId, CancellationToken cancellationToken) =>
        _recepcionRepo.ObtenerUltimaRecepcionResumenAsync(ordenCompraId, cancellationToken);

    Task<bool> IRecepcionRepository.ExisteRecepcionCompletaAsync(Guid ordenCompraId, CancellationToken cancellationToken) =>
        _recepcionRepo.ExisteRecepcionCompletaAsync(ordenCompraId, cancellationToken);

    Task<bool> IRecepcionRepository.ExisteNumeroRemitoAsync(Guid ordenCompraId, string numeroRemito, CancellationToken cancellationToken) =>
        _recepcionRepo.ExisteNumeroRemitoAsync(ordenCompraId, numeroRemito, cancellationToken);

    Task<Dictionary<Guid, decimal>> IRecepcionRepository.ObtenerCantidadesRecibidasPorArticuloAsync(Guid ordenCompraId, CancellationToken cancellationToken) =>
        _recepcionRepo.ObtenerCantidadesRecibidasPorArticuloAsync(ordenCompraId, cancellationToken);

    void IRecepcionRepository.AgregarRecepcion(Domain.Entities.Recepcion recepcion) =>
        _recepcionRepo.AgregarRecepcion(recepcion);

    Task<bool> IPartidaPresupuestariaRepository.ExisteCodigoPartidaAsync(Guid obraId, string codigoNormalizadoLower, CancellationToken cancellationToken) =>
        _partidaRepo.ExisteCodigoPartidaAsync(obraId, codigoNormalizadoLower, cancellationToken);

    Task<IReadOnlyList<string?>> IPartidaPresupuestariaRepository.ObtenerEtapasPorObraAsync(Guid obraId, CancellationToken cancellationToken) =>
        _partidaRepo.ObtenerEtapasPorObraAsync(obraId, cancellationToken);

    Task<List<PartidaPresupuestaria>> IPartidaPresupuestariaRepository.ObtenerPartidasPorObraParaActualizarAsync(Guid obraId, CancellationToken cancellationToken) =>
        _partidaRepo.ObtenerPartidasPorObraParaActualizarAsync(obraId, cancellationToken);

    Task IPartidaPresupuestariaRepository.AgregarPartidaAsync(PartidaPresupuestaria partida, CancellationToken cancellationToken) =>
        _partidaRepo.AgregarPartidaAsync(partida, cancellationToken);

    Task<PaginaDe<RecepcionDto>> IRecepcionQueries.ObtenerRecepcionesAsync(ObtenerRecepcionesQuery query, CancellationToken cancellationToken) =>
        _recepcionQueries.ObtenerRecepcionesAsync(query, cancellationToken);

    Task<RecepcionDto?> IRecepcionQueries.ObtenerRecepcionPorIdAsync(Guid recepcionId, CancellationToken cancellationToken) =>
        _recepcionQueries.ObtenerRecepcionPorIdAsync(recepcionId, cancellationToken);

    Task<RecepcionesDeOrdenDto?> IRecepcionQueries.ObtenerRecepcionesPorOrdenAsync(Guid ordenCompraId, CancellationToken cancellationToken) =>
        _recepcionQueries.ObtenerRecepcionesPorOrdenAsync(ordenCompraId, cancellationToken);

    Task<FotoDescargaDto?> IRecepcionQueries.ObtenerFotoRecepcionAsync(Guid recepcionId, Guid fotoId, CancellationToken cancellationToken) =>
        _recepcionQueries.ObtenerFotoRecepcionAsync(recepcionId, fotoId, cancellationToken);

    Task<PaginaDe<OrdenPendienteDto>> IRecepcionQueries.ObtenerOrdenesPendientesAsync(ObtenerOrdenesPendientesQuery query, CancellationToken cancellationToken) =>
        _recepcionQueries.ObtenerOrdenesPendientesAsync(query, cancellationToken);

    Task<PaginaDe<OrdenBloqueadaDto>> IRecepcionQueries.ObtenerOrdenesBloqueadasAsync(ObtenerOrdenesBloqueadasQuery query, CancellationToken cancellationToken) =>
        _recepcionQueries.ObtenerOrdenesBloqueadasAsync(query, cancellationToken);

    Task<PaginaDe<PartidaPresupuestariaDto>> IPartidaQueries.ObtenerPartidasAsync(ObtenerPartidasQuery query, CancellationToken cancellationToken) =>
        _partidaQueries.ObtenerPartidasAsync(query, cancellationToken);

    Task<PartidaPresupuestariaDto?> IPartidaQueries.ObtenerPartidaPorIdAsync(Guid partidaId, CancellationToken cancellationToken) =>
        _partidaQueries.ObtenerPartidaPorIdAsync(partidaId, cancellationToken);
}
