using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Asistente.Api.Dtos;
using ObraSync.Asistente.Api.Servicios;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Api.Controllers;

[ApiController]
[Route("api/asistente")]
// Contrato v1: en la primera versión solo Capataz y Admin. Un rol sin perfil recibe 403 y NO se llama al modelo (#143).
[Authorize(Roles = "Capataz,Admin")]
public sealed class AsistenteController : ControllerBase
{
    private readonly Agente _agente;
    private readonly IContextoDePeticion _contexto;
    private readonly ILogger<AsistenteController> _log;

    public AsistenteController(Agente agente, IContextoDePeticion contexto, ILogger<AsistenteController> log)
    {
        _agente = agente;
        _contexto = contexto;
        _log = log;
    }

    /// <summary>Envía un mensaje al asistente y devuelve su respuesta (el asistente nunca escribe datos).</summary>
    [HttpPost("chat")]
    [ProducesResponseType(typeof(ChatResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Chat([FromBody] ChatRequestDto pedido, CancellationToken ct)
    {
        var error = ValidadorChat.Validar(pedido);
        if (error is not null)
            return Problema(StatusCodes.Status400BadRequest, "Pedido inválido", error);

        var historial = (pedido.Historial ?? new List<ChatMensajeDto>())
            .Select(m => new MensajeHistorial(m.Rol!, m.Texto!))
            .ToList();

        var r = await _agente.ResponderAsync(pedido.Mensaje!.Trim(), historial, ct);

        if (r.Ok)
            return Ok(new ChatResponseDto(r.Texto, r.Borrador, r.Tokens));

        // Se anota el código estable (nunca la clave ni el texto crudo del proveedor).
        _log.LogWarning("Asistente sin respuesta: {Codigo} (iteraciones={It}, herramientas={Herr}, tokens={Tok}) correlationId={CorrelationId}",
            r.Error, r.Iteraciones, r.Herramientas, r.Tokens, _contexto.CorrelationId);

        return r.Error switch
        {
            "modelo_limite_de_tasa" => Problema(StatusCodes.Status429TooManyRequests, "Asistente ocupado",
                "El asistente está ocupado. Probá de nuevo en un rato."),

            // El agente se pasó de sus límites: no es una caída, es una consulta que no pudo cerrar.
            "demasiadas_iteraciones" or "demasiadas_herramientas" or "tiempo_total_agotado" =>
                Ok(new ChatResponseDto(
                    "No pude terminar esa consulta. Probá con una pregunta más corta o más concreta.", null, r.Tokens)),

            "entrada_vacia" or "entrada_demasiado_larga" => Problema(StatusCodes.Status400BadRequest, "Pedido inválido",
                "El mensaje está vacío o supera el máximo de caracteres."),

            // asistente_apagado, modelo_no_disponible, modelo_timeout, modelo_clave_invalida, modelo_sin_permiso_o_cupo, etc.
            _ => Problema(StatusCodes.Status503ServiceUnavailable, "Asistente no disponible",
                "El asistente no está disponible por ahora. Probá más tarde.")
        };
    }

    private ObjectResult Problema(int estado, string titulo, string detalle)
    {
        var problema = new ProblemDetails
        {
            Type = estado switch
            {
                StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                StatusCodes.Status429TooManyRequests => "https://tools.ietf.org/html/rfc6585#section-4",
                _ => "https://tools.ietf.org/html/rfc9110#section-15.6.4"
            },
            Title = titulo,
            Status = estado,
            Detail = detalle,
            Instance = Request.Path
        };
        problema.Extensions["correlationId"] = _contexto.CorrelationId;
        return new ObjectResult(problema)
        {
            StatusCode = estado,
            ContentTypes = { "application/problem+json" }
        };
    }
}
