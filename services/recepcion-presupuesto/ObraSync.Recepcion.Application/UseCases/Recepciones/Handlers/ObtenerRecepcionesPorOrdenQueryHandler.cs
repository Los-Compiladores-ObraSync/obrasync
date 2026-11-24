using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Mappings;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;
using ObraSync.Recepcion.Domain.Common;
using ObraSync.Recepcion.Domain.Constants;
using ObraSync.Recepcion.Domain.Enums;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

/// <summary>
/// Obtiene el historial completo de recepciones (parciales, completas y entregas con problema
/// con su motivo y metadatos de fotos), junto con los datos de despacho y saldos de una orden (#217).
/// </summary>
public class ObtenerRecepcionesPorOrdenQueryHandler
{
    private readonly IRecepcionDbContext _context;

    public ObtenerRecepcionesPorOrdenQueryHandler(IRecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<RecepcionesDeOrdenDto?> Handle(
        ObtenerRecepcionesPorOrdenQuery query,
        CancellationToken cancellationToken = default)
    {
        var orden = await _context.OrdenesCompra
            .Include(o => o.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == query.OrdenCompraId, cancellationToken);

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

        // Acumulados por material EXCLUYENDO siempre entregas con problema (Rechazada)
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

        bool tieneProblemaAbierto = ultimaRecepcion?.Estado == EstadoRecepcion.Rechazada;
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
            EsperaNuevoDespacho = !estaCompleta && orden.FechaDespacho is { } fechaDespacho &&
                recepciones.Any(r => (r.Estado == EstadoRecepcion.Parcial || r.Estado == EstadoRecepcion.Rechazada) &&
                                   r.CreatedAt >= fechaDespacho),
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

    private sealed record FotoMetadatoRow(
        Guid Id,
        Guid RecepcionId,
        string NombreArchivo,
        string ContentType,
        long TamanoBytes,
        int Orden);
}
