using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ObraSync.Asistente.Api.Dtos;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Api.Controllers;

[ApiController]
[Route("api/asistente/remitos")]
// Solo quien recibe mercadería: Capataz y Admin. El resto recibe 403 y NO se llama al modelo.
[Authorize(Roles = "Capataz,Admin")]
public sealed class RemitosController : ControllerBase
{
    private readonly ServicioLecturaRemito _servicio;
    private readonly IContextoDePeticion _contexto;
    private readonly ILogger<RemitosController> _log;

    public RemitosController(ServicioLecturaRemito servicio, IContextoDePeticion contexto, ILogger<RemitosController> log)
    {
        _servicio = servicio;
        _contexto = contexto;
        _log = log;
    }

    /// <summary>
    /// Registra una lectura del número y la fecha a partir de la foto de un remito (multipart/form-data, campo "imagen").
    /// No guarda la imagen ni escribe datos: devuelve una sugerencia para completar el formulario de Recepción.
    /// POST /api/asistente/remitos/lecturas
    /// </summary>
    [HttpPost("lecturas")]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting("remito-por-usuario")]
    // Un poco más que el máximo de la imagen: el multipart agrega sus encabezados.
    [RequestSizeLimit(ValidadorImagenRemito.MaxBytes + 64 * 1024)]
    [ProducesResponseType(typeof(LecturaRemitoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Leer(IFormFile? imagen, CancellationToken ct)
    {
        if (imagen is null)
            return Problema(StatusCodes.Status400BadRequest, "Pedido inválido", "Falta la imagen del remito (campo 'imagen').");
        // Se corta antes de leer el archivo entero a memoria si ya declara ser demasiado grande.
        if (imagen.Length > ValidadorImagenRemito.MaxBytes)
            return Problema(StatusCodes.Status400BadRequest, "Pedido inválido", "La imagen no puede pesar más de 5 MB.");

        byte[] bytes;
        await using (var flujo = imagen.OpenReadStream())
        using (var memoria = new MemoryStream())
        {
            await flujo.CopyToAsync(memoria, ct);
            bytes = memoria.ToArray();
        }

        var (tipoMedia, error) = ValidadorImagenRemito.Validar(bytes);
        if (error is not null)
            return Problema(StatusCodes.Status400BadRequest, "Pedido inválido", error);

        var r = await _servicio.LeerAsync(bytes, tipoMedia!, ct);

        if (r.Lectura is not null)
        {
            // Registro por pedido (#144): solo tokens, rol y correlationId. Nunca la imagen ni lo que se leyó.
            _log.LogInformation("Remito consumo: tokens={Tok} rol={Rol} numeroLeido={Numero} fechaLeida={Fecha} correlationId={CorrelationId}",
                r.Tokens, _contexto.Rol, r.Lectura.NumeroRemito is not null, r.Lectura.Fecha is not null, _contexto.CorrelationId);
            return Ok(new LecturaRemitoDto(r.Lectura.NumeroRemito, r.Lectura.Fecha?.ToString("yyyy-MM-dd")));
        }

        _log.LogWarning("Remito sin lectura: {Codigo} rol={Rol} correlationId={CorrelationId}", r.Error, _contexto.Rol, _contexto.CorrelationId);
        return r.Error switch
        {
            "modelo_limite_de_tasa" or "asistente_ocupado" => Problema(StatusCodes.Status429TooManyRequests, "Asistente ocupado",
                "El asistente está ocupado. Probá de nuevo en un minuto."),
            _ => Problema(StatusCodes.Status503ServiceUnavailable, "Lectura no disponible",
                "No se pudo leer el remito por ahora. Cargalo a mano o probá más tarde.")
        };
    }

    private ObjectResult Problema(int estado, string titulo, string detalle)
    {
        if (estado == StatusCodes.Status429TooManyRequests)
            Response.Headers.RetryAfter = "60";

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
