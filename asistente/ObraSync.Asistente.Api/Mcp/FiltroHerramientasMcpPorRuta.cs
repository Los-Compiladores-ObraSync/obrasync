using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Api.Mcp;

/// <summary>
/// Aísla el endpoint de presupuestos en el servidor MCP compartido: filtra tanto el listado
/// como la ejecución para que ese path solo exponga consultar_partidas_presupuestarias.
/// </summary>
public static class FiltroHerramientasMcpPorRuta
{
    public const string RutaOriginal = "/api/mcp";
    public const string RutaPresupuestos = "/api/mcp/presupuestos";

    public static void Configurar(IMcpRequestFilterBuilder filtros)
    {
        filtros.AddListToolsFilter(siguiente => async (contexto, cancellationToken) =>
        {
            var ruta = ClasificarRuta(contexto.Services);
            if (ruta == RutaMcp.Desconocida)
                return new ListToolsResult { Tools = [] };

            var resultado = await siguiente(contexto, cancellationToken);
            if (ruta == RutaMcp.Presupuestos)
            {
                resultado.Tools = resultado.Tools
                    .Where(herramienta => herramienta.Name == HerramientasPresupuestos.ConsultarPartidas)
                    .ToList();
            }

            return resultado;
        });

        filtros.AddCallToolFilter(siguiente => async (contexto, cancellationToken) =>
        {
            var ruta = ClasificarRuta(contexto.Services);
            if (ruta == RutaMcp.Desconocida ||
                (ruta == RutaMcp.Presupuestos &&
                 contexto.Params.Name != HerramientasPresupuestos.ConsultarPartidas))
            {
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = "La llamada MCP no está permitida en esta ruta." }],
                    IsError = true
                };
            }

            return await siguiente(contexto, cancellationToken);
        });
    }

    private static RutaMcp ClasificarRuta(IServiceProvider? servicios)
    {
        if (servicios?.GetService(typeof(IHttpContextAccessor)) is not IHttpContextAccessor accessor ||
            accessor.HttpContext is not { } httpContext)
            return RutaMcp.Desconocida;

        var path = httpContext.Request.Path;
        if (path.Equals(RutaOriginal, StringComparison.OrdinalIgnoreCase))
            return RutaMcp.Original;
        if (path.StartsWithSegments(RutaPresupuestos, StringComparison.OrdinalIgnoreCase))
            return RutaMcp.Presupuestos;

        return RutaMcp.Desconocida;
    }

    private enum RutaMcp { Desconocida, Original, Presupuestos }
}
