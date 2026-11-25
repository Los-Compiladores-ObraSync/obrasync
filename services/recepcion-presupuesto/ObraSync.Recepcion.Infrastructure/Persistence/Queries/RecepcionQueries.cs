using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.Mappings;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;
using ObraSync.Recepcion.Domain.Common;
using ObraSync.Recepcion.Domain.Constants;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Domain.Presupuestos;

namespace ObraSync.Recepcion.Infrastructure.Persistence.Queries;

public class RecepcionQueries : IRecepcionQueries
{
    private readonly RecepcionDbContext _context;

    public RecepcionQueries(RecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PaginaDe<RecepcionDto>> ObtenerRecepcionesAsync(
        ObtenerRecepcionesQuery query,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _context.Recepciones.AsNoTracking();

        if (query.ObraId.HasValue)
        {
            dbQuery = dbQuery.Where(r => r.ObraId == query.ObraId.Value);
        }
        else if (query.ObrasPermitidas != null)
        {
            dbQuery = dbQuery.Where(r => query.ObrasPermitidas.Contains(r.ObraId));
        }

        if (query.OrdenCompraId.HasValue)
        {
            dbQuery = dbQuery.Where(r => r.OrdenCompraId == query.OrdenCompraId.Value);
        }

        var total = await dbQuery.CountAsync(cancellationToken);

        var p = query.Paginacion ?? new ParametrosPagina();
        var recepciones = await dbQuery
            .Include(r => r.Detalles)
            .OrderByDescending(r => r.FechaRecepcion)
            .ThenByDescending(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Skip(p.Saltear)
            .Take(p.Tamano)
            .ToListAsync(cancellationToken);

        var recepcionIds = recepciones.Select(r => r.Id).ToList();

        var fotosMetadatos = recepcionIds.Count == 0
            ? new List<FotoMetadatoRow>()
            : await _context.FotosRecepcion
                .AsNoTracking()
                .Where(f => recepcionIds.Contains(f.RecepcionId))
                .OrderBy(f => f.Orden)
                .ThenBy(f => f.CreatedAt)
                .Select(f => new FotoMetadatoRow(
                    f.Id,
                    f.RecepcionId,
                    f.NombreArchivo,
                    f.ContentType,
                    f.TamanoBytes,
                    f.Orden))
                .ToListAsync(cancellationToken);

        var fotosPorRecepcion = fotosMetadatos
            .GroupBy(f => f.RecepcionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<FotoRecepcionDto>)g
                    .Select(f => RecepcionMappings.CrearFotoDto(
                        f.Id, f.RecepcionId, f.NombreArchivo, f.ContentType, f.TamanoBytes, f.Orden))
                    .ToList());

        var dtos = recepciones
            .Select(r => r.ToDto(
                fotosPorRecepcion.TryGetValue(r.Id, out var fotos) ? fotos : Array.Empty<FotoRecepcionDto>()))
            .ToList();

        return new PaginaDe<RecepcionDto>(dtos, total);
    }

    public async Task<RecepcionDto?> ObtenerRecepcionPorIdAsync(
        Guid recepcionId,
        CancellationToken cancellationToken = default)
    {
        var recepcion = await _context.Recepciones
            .Include(r => r.Detalles)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recepcionId, cancellationToken);

        if (recepcion is null)
        {
            return null;
        }

        var fotos = await _context.FotosRecepcion
            .AsNoTracking()
            .Where(f => f.RecepcionId == recepcion.Id)
            .OrderBy(f => f.Orden)
            .ThenBy(f => f.CreatedAt)
            .Select(f => RecepcionMappings.CrearFotoDto(
                f.Id,
                f.RecepcionId,
                f.NombreArchivo,
                f.ContentType,
                f.TamanoBytes,
                f.Orden))
            .ToListAsync(cancellationToken);

        return recepcion.ToDto(fotos);
    }

    public async Task<RecepcionesDeOrdenDto?> ObtenerRecepcionesPorOrdenAsync(
        Guid ordenCompraId,
        CancellationToken cancellationToken = default)
    {
        var orden = await _context.OrdenesCompra
            .Include(o => o.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == ordenCompraId, cancellationToken);

        if (orden is null)
        {
            return null;
        }

        var recepciones = await _context.Recepciones
            .AsNoTracking()
            .Include(r => r.Detalles)
            .Where(r => r.OrdenCompraId == orden.Id)
            .OrderBy(r => r.FechaRecepcion)
            .ThenBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var recepcionIds = recepciones.Select(r => r.Id).ToList();

        var fotosMetadatos = recepcionIds.Count == 0
            ? new List<FotoMetadatoRow>()
            : await _context.FotosRecepcion
                .AsNoTracking()
                .Where(f => recepcionIds.Contains(f.RecepcionId))
                .OrderBy(f => f.Orden)
                .ThenBy(f => f.CreatedAt)
                .Select(f => new FotoMetadatoRow(
                    f.Id,
                    f.RecepcionId,
                    f.NombreArchivo,
                    f.ContentType,
                    f.TamanoBytes,
                    f.Orden))
                .ToListAsync(cancellationToken);

        var fotosPorRecepcion = fotosMetadatos
            .GroupBy(f => f.RecepcionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<FotoRecepcionDto>)g
                    .Select(f => RecepcionMappings.CrearFotoDto(
                        f.Id, f.RecepcionId, f.NombreArchivo, f.ContentType, f.TamanoBytes, f.Orden))
                    .ToList());

        var recibidosPorArticulo = recepciones
            .Where(r => r.Estado != EstadoRecepcion.Rechazada)
            .SelectMany(r => r.Detalles)
            .GroupBy(d => d.ArticuloId)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.CantidadRecibida));

        var saldosPendientes = orden.CalcularSaldosPendientes(recibidosPorArticulo);

        var itemsDto = orden.Items
            .Select(i =>
            {
                decimal recibido = recibidosPorArticulo.GetValueOrDefault(i.ArticuloId, 0m);
                decimal pendiente = saldosPendientes.GetValueOrDefault(i.ArticuloId, Math.Max(0m, i.Cantidad - recibido));

                return new ItemOrdenPendienteDto
                {
                    ArticuloId = i.ArticuloId,
                    DescripcionArticulo = i.Descripcion ?? string.Empty,
                    CantidadEsperada = i.Cantidad,
                    CantidadRecibida = recibido,
                    CantidadPendiente = pendiente,
                    UnidadMedida = i.UnidadMedida
                };
            })
            .ToList();

        bool estaCompleta = recepciones.Any(r => r.Estado == EstadoRecepcion.Completa);

        var ultimaRecepcion = recepciones
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.FechaRecepcion)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();

        bool tieneProblemaAbierto = ultimaRecepcion?.Estado == EstadoRecepcion.Rechazada
            && orden.EsperaNuevoDespachoTras(ultimaRecepcion.CreatedAt);
        bool tieneRecepcionParcial = recepciones.Any(r => r.Estado == EstadoRecepcion.Parcial);

        return new RecepcionesDeOrdenDto
        {
            OrdenCompraId = orden.Id,
            ObraId = orden.ObraId,
            ProveedorId = orden.ProveedorId,
            Etapa = orden.Etapa,
            FechaDespacho = FechaUtc.Normalizar(orden.FechaDespacho),
            FechaEstimadaLlegada = FechaUtc.Normalizar(orden.FechaEstimadaLlegada),
            ObservacionDespacho = orden.ObservacionDespacho,
            EstaCompleta = estaCompleta,
            TieneProblemaAbierto = tieneProblemaAbierto,
            EsperaNuevoDespacho = tieneProblemaAbierto,
            BloqueadaPorPresupuesto = orden.EstaBloqueada,
            EstadoEntrega = EstadosEntregaOrden.Calcular(
                estaCompleta: estaCompleta,
                tieneProblemaAbierto: tieneProblemaAbierto,
                tieneRecepcionParcial: tieneRecepcionParcial),
            Items = itemsDto,
            Recepciones = recepciones
                .Select(r => r.ToDto(
                    fotosPorRecepcion.TryGetValue(r.Id, out var fotos) ? fotos : Array.Empty<FotoRecepcionDto>()))
                .ToList()
        };
    }

    public async Task<FotoDescargaDto?> ObtenerFotoRecepcionAsync(
        Guid recepcionId,
        Guid fotoId,
        CancellationToken cancellationToken = default)
    {
        var foto = await _context.FotosRecepcion
            .AsNoTracking()
            .Where(f => f.Id == fotoId && f.RecepcionId == recepcionId)
            .Select(f => new
            {
                f.Id,
                f.RecepcionId,
                f.NombreArchivo,
                f.ContentType,
                f.Contenido,
                f.Recepcion.ObraId,
                f.Recepcion.OrdenCompraId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (foto is null)
        {
            return null;
        }

        var proveedorId = await _context.OrdenesCompra
            .AsNoTracking()
            .Where(o => o.Id == foto.OrdenCompraId)
            .Select(o => (Guid?)o.ProveedorId)
            .FirstOrDefaultAsync(cancellationToken);

        return new FotoDescargaDto(
            foto.Id,
            foto.RecepcionId,
            foto.ObraId,
            foto.OrdenCompraId,
            proveedorId,
            foto.NombreArchivo,
            foto.ContentType,
            foto.Contenido);
    }

    public async Task<PaginaDe<OrdenPendienteDto>> ObtenerOrdenesPendientesAsync(
        ObtenerOrdenesPendientesQuery query,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _context.OrdenesCompra
            .Include(o => o.Items)
            .AsNoTracking()
            .Where(o => o.Estado == EstadoOrdenCompra.Activa &&
                        o.FechaDespacho != null &&
                        !_context.Recepciones.Any(
                            r => r.OrdenCompraId == o.Id &&
                                 r.Estado == EstadoRecepcion.Completa));

        if (query.ObraId.HasValue)
        {
            dbQuery = dbQuery.Where(o => o.ObraId == query.ObraId.Value);
        }
        else if (query.ObrasPermitidas != null)
        {
            dbQuery = dbQuery.Where(o => query.ObrasPermitidas.Contains(o.ObraId));
        }

        if (query.SinPartida.HasValue)
        {
            dbQuery = dbQuery.Where(o => o.SinPartidaPresupuestaria == query.SinPartida.Value);
        }

        var total = await dbQuery.CountAsync(cancellationToken);

        var p = query.Paginacion ?? new ParametrosPagina();
        var ordenes = await dbQuery
            .OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .Skip(p.Saltear)
            .Take(p.Tamano)
            .ToListAsync(cancellationToken);

        var ordenesIds = ordenes.Select(o => o.Id).ToList();

        var recibidos = await _context.Recepciones
            .AsNoTracking()
            .Where(r => ordenesIds.Contains(r.OrdenCompraId) &&
                        r.Estado != EstadoRecepcion.Rechazada)
            .SelectMany(r => r.Detalles.Select(d => new
            {
                r.OrdenCompraId,
                d.ArticuloId,
                d.CantidadRecibida
            }))
            .ToListAsync(cancellationToken);

        var ultimosEstados = await _context.Recepciones
            .AsNoTracking()
            .Where(r => ordenesIds.Contains(r.OrdenCompraId))
            .Select(r => new
            {
                r.OrdenCompraId,
                r.Estado,
                r.CreatedAt,
                r.FechaRecepcion,
                r.Id
            })
            .ToListAsync(cancellationToken);

        var ultimaPorOrden = ultimosEstados
            .GroupBy(r => r.OrdenCompraId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.CreatedAt)
                      .ThenByDescending(x => x.FechaRecepcion)
                      .ThenByDescending(x => x.Id)
                      .First());

        var ordenesConEntregaParcial = ultimosEstados
            .Where(r => r.Estado == EstadoRecepcion.Parcial)
            .Select(r => r.OrdenCompraId)
            .ToHashSet();

        var items = ordenes
            .Select(o =>
            {
                var recibidosDeEstaOrden = recibidos
                    .Where(d => d.OrdenCompraId == o.Id)
                    .GroupBy(d => d.ArticuloId)
                    .ToDictionary(g => g.Key, g => g.Sum(d => d.CantidadRecibida));

                var saldosPendientes = o.CalcularSaldosPendientes(recibidosDeEstaOrden);
                bool tieneProblemaAbierto = ultimaPorOrden.TryGetValue(o.Id, out var ultima)
                    && ultima.Estado == EstadoRecepcion.Rechazada
                    && o.EsperaNuevoDespachoTras(ultima.CreatedAt);
                bool tieneEntregaParcial = ordenesConEntregaParcial.Contains(o.Id);

                return new OrdenPendienteDto
                {
                    OrdenCompraId = o.Id,
                    ObraId = o.ObraId,
                    Etapa = o.Etapa,
                    SinPartidaPresupuestaria = o.SinPartidaPresupuestaria,
                    FechaDespacho = FechaUtc.Normalizar(o.FechaDespacho),
                    FechaEstimadaLlegada = FechaUtc.Normalizar(o.FechaEstimadaLlegada),
                    ObservacionDespacho = o.ObservacionDespacho,
                    TieneProblemaAbierto = tieneProblemaAbierto,
                    EsperaNuevoDespacho = tieneProblemaAbierto,
                    EstadoEntrega = EstadosEntregaOrden.Calcular(
                        estaCompleta: false,
                        tieneProblemaAbierto: tieneProblemaAbierto,
                        tieneRecepcionParcial: tieneEntregaParcial),
                    Items = o.Items
                        .Select(i =>
                        {
                            decimal recibido = recibidosDeEstaOrden.GetValueOrDefault(i.ArticuloId, 0m);
                            decimal pendiente = saldosPendientes.GetValueOrDefault(i.ArticuloId, Math.Max(0m, i.Cantidad - recibido));

                            return new ItemOrdenPendienteDto
                            {
                                ArticuloId = i.ArticuloId,
                                DescripcionArticulo = i.Descripcion ?? string.Empty,
                                CantidadEsperada = i.Cantidad,
                                CantidadRecibida = recibido,
                                CantidadPendiente = pendiente,
                                UnidadMedida = i.UnidadMedida
                            };
                        })
                        .ToList()
                };
            })
            .ToList();

        return new PaginaDe<OrdenPendienteDto>(items, total);
    }

    public async Task<PaginaDe<OrdenBloqueadaDto>> ObtenerOrdenesBloqueadasAsync(
        ObtenerOrdenesBloqueadasQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var dbQuery = _context.OrdenesCompra
            .AsNoTracking()
            .Where(o => o.Estado == EstadoOrdenCompra.Bloqueada);

        if (query.ObraId.HasValue && query.ObraId.Value != Guid.Empty)
        {
            dbQuery = dbQuery.Where(o => o.ObraId == query.ObraId.Value);
        }

        var total = await dbQuery.CountAsync(cancellationToken);

        var p = query.Paginacion ?? new ParametrosPagina();
        var ordenes = await dbQuery
            .OrderByDescending(o => o.FechaBloqueo ?? o.UpdatedAt ?? o.CreatedAt)
            .ThenByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .Skip(p.Saltear)
            .Take(p.Tamano)
            .ToListAsync(cancellationToken);

        if (ordenes.Count == 0)
        {
            return new PaginaDe<OrdenBloqueadaDto>(Array.Empty<OrdenBloqueadaDto>(), total);
        }

        var obraIdsPagina = ordenes.Select(o => o.ObraId).Distinct().ToList();
        var partidasPorObra = (await _context.PartidasPresupuestarias
                .AsNoTracking()
                .Where(partida => obraIdsPagina.Contains(partida.ObraId))
                .ToListAsync(cancellationToken))
            .GroupBy(partida => partida.ObraId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var items = ordenes.Select(oc =>
        {
            partidasPorObra.TryGetValue(oc.ObraId, out var partidasObra);
            var partida = EmparejadorPartidaPresupuestaria.BuscarPartida(partidasObra, oc.Etapa);
            return oc.ToBloqueadaDto(partida);
        }).ToList();

        return new PaginaDe<OrdenBloqueadaDto>(items, total);
    }

    private sealed record FotoMetadatoRow(
        Guid Id,
        Guid RecepcionId,
        string NombreArchivo,
        string ContentType,
        long TamanoBytes,
        int Orden);
}
