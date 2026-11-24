using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Exceptions;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Application.UseCases.Requisiciones.Handlers;

public class CreateRequisicionCommandHandler
{
    public static readonly TimeSpan VentanaDuplicados = TimeSpan.FromMinutes(2);

    private readonly IRequisicionRepository _requisicionRepository;
    private readonly IInsumoRepository _insumoRepository;
    private readonly IObraAsignacionValidator? _obraAsignacionValidator;

    public CreateRequisicionCommandHandler(
        IRequisicionRepository requisicionRepository,
        IInsumoRepository insumoRepository,
        IObraAsignacionValidator? obraAsignacionValidator = null)
    {
        _requisicionRepository = requisicionRepository ?? throw new ArgumentNullException(nameof(requisicionRepository));
        _insumoRepository = insumoRepository ?? throw new ArgumentNullException(nameof(insumoRepository));
        _obraAsignacionValidator = obraAsignacionValidator;
    }

    public async Task<RequisicionResponseDto> Handle(
        CreateRequisicionCommand command,
        CancellationToken cancellationToken = default)
    {
        var idempotencyKey = ValidarEntrada(command);
        await ValidarObraAsignadaAsync(command, cancellationToken);

        var itemsSolicitados = command.Items.Select(i => (i.ArticuloId, i.Cantidad)).ToList();

        if (await BuscarPorIdempotencyKeyAsync(command, idempotencyKey, itemsSolicitados, cancellationToken) is { } existente)
            return existente;

        var ahoraUtc = DateTime.UtcNow;
        if (!command.RepeticionIntencional || idempotencyKey is null)
        {
            await AsegurarSinDuplicadoRecienteAsync(command, itemsSolicitados, ahoraUtc, cancellationToken);
        }

        var requisicionId = Guid.NewGuid();
        var itemsDominio = await _insumoRepository.ConstruirItemsAsync(requisicionId, command.Items, cancellationToken);
        var secuencia = await _requisicionRepository.GetNextSequenceNumberAsync(cancellationToken);

        var requisicion = new Requisicion(
            requisicionId,
            Requisicion.FormatearNumero(ahoraUtc.Year, secuencia),
            command.ObraId,
            command.SolicitanteId,
            itemsDominio,
            command.Etapa,
            command.Observaciones,
            ahoraUtc,
            idempotencyKey);

        return await GuardarResolviendoIdempotenciaAsync(
            requisicion,
            command,
            idempotencyKey,
            itemsSolicitados,
            cancellationToken);
    }

    private static string? ValidarEntrada(CreateRequisicionCommand command)
    {
        if (command.ObraId == Guid.Empty)
            throw new ArgumentException("El identificador de la obra (obraId) es obligatorio.", nameof(command));

        if (command.SolicitanteId == Guid.Empty)
            throw new ArgumentException("El identificador del solicitante es obligatorio.", nameof(command));

        CatalogoItemsResolver.ExtraerArticuloIdsValidos(command.Items);
        return Requisicion.NormalizarIdempotencyKey(command.IdempotencyKey);
    }

    private async Task ValidarObraAsignadaAsync(CreateRequisicionCommand command, CancellationToken cancellationToken)
    {
        // #155: el Capataz solo puede crear requisiciones para obras que tiene asignadas
        if (!command.ValidarObraAsignada || _obraAsignacionValidator is null)
            return;

        var asignada = await _obraAsignacionValidator.EstaAsignadaACapatazAsync(
            command.ObraId,
            command.AuthorizationHeader,
            command.CorrelationId,
            cancellationToken);

        if (!asignada)
        {
            throw new ObraNoAsignadaException(
                "El capataz autenticado no tiene asignada la obra indicada para crear requisiciones.");
        }
    }

    private async Task<RequisicionResponseDto?> BuscarPorIdempotencyKeyAsync(
        CreateRequisicionCommand command,
        string? idempotencyKey,
        IReadOnlyList<(Guid ArticuloId, decimal Cantidad)> itemsSolicitados,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey is null)
            return null;

        var existente = await _requisicionRepository.GetByIdempotencyKeyAsync(
            command.SolicitanteId,
            idempotencyKey,
            cancellationToken);

        if (existente is null)
            return null;

        if (existente.CoincideConSolicitud(command.SolicitanteId, command.ObraId, command.Etapa, itemsSolicitados))
            return existente.ToDto();

        throw new RequisicionDuplicadaException(
            existente.NumeroRequisicion,
            existente.Id,
            $"El encabezado Idempotency-Key '{idempotencyKey}' ya fue utilizado en la requisición '{existente.NumeroRequisicion}' con datos distintos.");
    }

    private async Task AsegurarSinDuplicadoRecienteAsync(
        CreateRequisicionCommand command,
        IReadOnlyList<(Guid ArticuloId, decimal Cantidad)> itemsSolicitados,
        DateTime ahoraUtc,
        CancellationToken cancellationToken)
    {
        var recientes = await _requisicionRepository.GetRecentBySolicitanteAndObraAsync(
            command.SolicitanteId,
            command.ObraId,
            ahoraUtc.Subtract(VentanaDuplicados),
            cancellationToken);

        var duplicada = recientes?.FirstOrDefault(r =>
            r.CoincideConSolicitud(command.SolicitanteId, command.ObraId, command.Etapa, itemsSolicitados));

        if (duplicada is not null)
        {
            throw new RequisicionDuplicadaException(
                duplicada.NumeroRequisicion,
                duplicada.Id,
                $"Ya existe una requisición idéntica creada recientemente ({duplicada.NumeroRequisicion}).");
        }
    }

    private async Task<RequisicionResponseDto> GuardarResolviendoIdempotenciaAsync(
        Requisicion requisicion,
        CreateRequisicionCommand command,
        string? idempotencyKey,
        IReadOnlyList<(Guid ArticuloId, decimal Cantidad)> itemsSolicitados,
        CancellationToken cancellationToken)
    {
        await _requisicionRepository.AddAsync(requisicion, cancellationToken);

        try
        {
            await _requisicionRepository.SaveChangesAsync(cancellationToken);
            return requisicion.ToDto();
        }
        catch (Exception) when (idempotencyKey is not null)
        {
            if (await BuscarPorIdempotencyKeyAsync(command, idempotencyKey, itemsSolicitados, cancellationToken) is { } concurrente)
                return concurrente;

            throw;
        }
    }
}
