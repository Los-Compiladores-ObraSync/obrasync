using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Api.Mcp;

/// <summary>
/// Aísla cada endpoint en el servidor MCP compartido aplicando la lista blanca de <see cref="PoliticaRutaMcp"/>:
/// filtra tanto el listado como la ejecución sin condicionales acoplados a rutas ni herramientas fijas (OCP).
/// </summary>
public static class FiltroHerramientasMcpPorRuta
{
    public const string RutaOriginal = "/api/mcp";
    public const string RutaPresupuestos = "/api/mcp/presupuestos";

    public static IReadOnlyList<PoliticaRutaMcp> PoliticasPorDefecto { get; } =
    [
        new PoliticaRutaMcp(
            RutaOriginal,
            "Capataz,Admin",
            [HerramientasCatalogo.BuscarInsumos, HerramientasCatalogo.ArmarBorrador],
            coincidenciaExacta: true),
        new PoliticaRutaMcp(
            RutaPresupuestos,
            "Comprador,Admin",
            [HerramientasPresupuestos.ConsultarPartidas])
    ];

    public static void Configurar(IMcpRequestFilterBuilder filtros)
    {
        filtros.AddListToolsFilter(siguiente => async (contexto, cancellationToken) =>
        {
            var politica = ResolverPolitica(contexto.Services);
            if (politica is null)
                return new ListToolsResult { Tools = [] };

            var resultado = await siguiente(contexto, cancellationToken);
            resultado.Tools = resultado.Tools
                .Where(herramienta => politica.PermiteHerramienta(herramienta.Name))
                .ToList();

            return resultado;
        });

        filtros.AddCallToolFilter(siguiente => async (contexto, cancellationToken) =>
        {
            var politica = ResolverPolitica(contexto.Services);
            if (politica is null || !politica.PermiteHerramienta(contexto.Params?.Name))
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

    public static PoliticaRutaMcp? ResolverPolitica(PathString path, IEnumerable<PoliticaRutaMcp> politicas) =>
        politicas.FirstOrDefault(p => p.CoincideRuta(path));

    private static PoliticaRutaMcp? ResolverPolitica(IServiceProvider? servicios)
    {
        if (servicios?.GetService<IHttpContextAccessor>() is not { HttpContext: { } httpContext })
            return null;

        var registradas = servicios.GetServices<PoliticaRutaMcp>().ToList();
        var politicas = registradas.Count > 0 ? registradas : PoliticasPorDefecto;
        return ResolverPolitica(httpContext.Request.Path, politicas);
    }
}
