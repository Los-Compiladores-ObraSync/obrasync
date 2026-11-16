using System;
using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.Mappings;
using ObraSync.Recepcion.Application.UseCases.Partidas.Commands;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Presupuestos;

namespace ObraSync.Recepcion.Application.UseCases.Partidas.Handlers;

public class CrearPartidaPresupuestariaCommandHandler
{
    private readonly IRecepcionDbContext _context;

    public CrearPartidaPresupuestariaCommandHandler(IRecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PartidaPresupuestariaDto> Handle(
        CrearPartidaPresupuestariaCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command == null || command.Request == null)
            throw new ArgumentNullException(nameof(command), "El comando de creación no puede ser nulo.");

        var req = command.Request;

        if (req.ObraId == Guid.Empty)
            throw new ArgumentException("El identificador de la obra es obligatorio.", nameof(req.ObraId));

        if (string.IsNullOrWhiteSpace(req.CodigoPartida))
            throw new ArgumentException("El código de partida es obligatorio.", nameof(req.CodigoPartida));

        if (string.IsNullOrWhiteSpace(req.Descripcion))
            throw new ArgumentException("La descripción de la partida es obligatoria.", nameof(req.Descripcion));

        if (req.MontoAsignado < 0)
            throw new ArgumentException("El monto asignado no puede ser negativo.", nameof(req.MontoAsignado));

        var codigoLimpio = req.CodigoPartida.Trim();
        var etapaLimpia = string.IsNullOrWhiteSpace(req.Etapa) ? null : req.Etapa.Trim();

        // Control de duplicados por código de partida y obra (#93, #195)
        var codigoNormalizado = codigoLimpio.ToLower();
        var codigoDuplicado = await _context.PartidasPresupuestarias
            .AnyAsync(p => p.ObraId == req.ObraId && p.CodigoPartida.ToLower() == codigoNormalizado, cancellationToken);

        if (codigoDuplicado)
        {
            throw new RecepcionConflictoException($"Ya existe una partida con el código '{codigoLimpio}' para esta obra.");
        }

        // Control de duplicados por obra y etapa (#195, #196): una sola partida por (ObraId, Etapa)
        // comparada de forma normalizada (sin distinguir mayúsculas, espacios ni tildes),
        // incluyendo la partida de toda la obra (Etapa == null).
        var etapasExistentes = await _context.PartidasPresupuestarias
            .Where(p => p.ObraId == req.ObraId)
            .Select(p => p.Etapa)
            .ToListAsync(cancellationToken);

        var etapaDuplicada = etapasExistentes.Any(e => NormalizadorEtapa.SonEquivalentes(e, etapaLimpia));

        if (etapaDuplicada)
        {
            var descripcionEtapa = etapaLimpia is not null
                ? $"la etapa '{etapaLimpia}'"
                : "toda la obra (sin etapa)";
            throw new RecepcionConflictoException($"Ya existe una partida presupuestaria para {descripcionEtapa} en esta obra.");
        }

        var partida = new PartidaPresupuestaria
        {
            Id = Guid.NewGuid(),
            ObraId = req.ObraId,
            Etapa = etapaLimpia,
            CodigoPartida = codigoLimpio,
            Descripcion = req.Descripcion.Trim(),
            MontoAsignado = req.MontoAsignado,
            MontoComprometido = 0m,
            MontoEjecutado = 0m,
            Moneda = string.IsNullOrWhiteSpace(req.Moneda) ? "ARS" : req.Moneda.Trim().ToUpperInvariant(),
            CreatedAt = DateTime.UtcNow
        };

        await _context.PartidasPresupuestarias.AddAsync(partida, cancellationToken);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (EsViolacionDeIndiceUnico(ex))
        {
            throw new RecepcionConflictoException(
                "Ya existe una partida presupuestaria con el mismo código o para la misma etapa en esta obra.");
        }

        return partida.ToDto();
    }

    private static bool EsViolacionDeIndiceUnico(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner == null)
        {
            return false;
        }

        if (inner.GetType().Name == "SqlException" &&
            inner.GetType().GetProperty("Number")?.GetValue(inner) is int sqlNumber)
        {
            return sqlNumber == 2601 || sqlNumber == 2627;
        }

        return false;
    }
}
