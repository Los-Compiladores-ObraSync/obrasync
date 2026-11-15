using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Api.Servicios;

/// <summary>Saca el JWT y el X-Correlation-ID del pedido HTTP que se está atendiendo.</summary>
public sealed class ContextoDePeticionHttp : IContextoDePeticion
{
    private const string Prefijo = "Bearer ";
    private readonly IHttpContextAccessor _accesor;

    public ContextoDePeticionHttp(IHttpContextAccessor accesor) => _accesor = accesor;

    public string? Jwt
    {
        get
        {
            var cabecera = _accesor.HttpContext?.Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(cabecera) || !cabecera.StartsWith(Prefijo, StringComparison.OrdinalIgnoreCase))
                return null;
            var token = cabecera[Prefijo.Length..].Trim();
            return token.Length == 0 ? null : token;
        }
    }

    public string CorrelationId
    {
        get
        {
            var contexto = _accesor.HttpContext;
            var valor = contexto?.Request.Headers["X-Correlation-ID"].ToString();
            if (!string.IsNullOrWhiteSpace(valor))
                return valor;
            return contexto?.TraceIdentifier ?? Guid.NewGuid().ToString();
        }
    }
}
