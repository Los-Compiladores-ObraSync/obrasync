using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ObraSync.Recepcion.Application.Interfaces;

/// <summary>
/// Contrato para validar si una obra está asignada al Capataz autenticado (#189, #155).
/// Permite desacoplar las llamadas HTTP hacia el microservicio Usuarios y Obras.
/// </summary>
public interface IObraAsignacionValidator
{
    /// <summary>
    /// Comprueba si la obra especificada pertenece a las obras asignadas al Capataz autenticado.
    /// </summary>
    Task<bool> EstaAsignadaACapatazAsync(
        Guid obraId,
        string? authorizationHeader,
        string? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene la lista de identificadores de obras asignadas al Capataz autenticado.
    /// Retorna null si la validación está deshabilitada (modo pruebas).
    /// </summary>
    Task<IReadOnlyList<Guid>?> ObtenerObrasAsignadasAsync(
        string? authorizationHeader,
        string? correlationId,
        CancellationToken cancellationToken = default);
}
