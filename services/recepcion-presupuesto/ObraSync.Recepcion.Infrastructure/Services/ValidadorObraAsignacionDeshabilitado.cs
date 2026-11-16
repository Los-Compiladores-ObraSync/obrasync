using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Recepcion.Application.Interfaces;

namespace ObraSync.Recepcion.Infrastructure.Services;

/// <summary>
/// Implementación de prueba que aprueba cualquier obra.
/// Solo se utiliza en pruebas unitarias/de integración aisladas cuando RabbitMQ:UseInMemory = true (#189, #178).
/// </summary>
public class ValidadorObraAsignacionDeshabilitado : IObraAsignacionValidator
{
    public Task<bool> EstaAsignadaACapatazAsync(
        Guid obraId,
        string? authorizationHeader,
        string? correlationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<IReadOnlyList<Guid>?> ObtenerObrasAsignadasAsync(
        string? authorizationHeader,
        string? correlationId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Guid>?>(null);
}
