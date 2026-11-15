using System;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Application.Interfaces;

namespace ObraSync.Requisiciones.Infrastructure.Services
{
    /// <summary>
    /// Deja pasar cualquier obra. Solo se registra en pruebas aisladas del servicio
    /// (RabbitMQ:UseInMemory = true y sin UsuariosObras:BaseUrl). Fuera de eso, falta de
    /// configuración hace fallar el arranque (#178) en vez de saltearse la validación.
    /// </summary>
    public class ValidadorObraAsignacionDeshabilitado : IObraAsignacionValidator
    {
        public Task<bool> EstaAsignadaACapatazAsync(
            Guid obraId,
            string? authorizationHeader,
            string? correlationId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
