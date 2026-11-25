using System.ComponentModel;
using ModelContextProtocol.Server;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Api.Mcp;

/// <summary>
/// Herramienta MCP de auditoría presupuestaria y órdenes bloqueadas (SRP y DIP).
/// Depende únicamente de la abstracción <see cref="IConsultaPresupuestoObra"/> sin acoplarse a las herramientas de Catálogo.
/// </summary>
[McpServerToolType]
public sealed class HerramientasPresupuestosMcp
{
    private readonly IConsultaPresupuestoObra _presupuestos;

    public HerramientasPresupuestosMcp(IConsultaPresupuestoObra presupuestos)
    {
        _presupuestos = presupuestos;
    }

    [McpServerTool(Name = HerramientasPresupuestos.ConsultarPartidas, ReadOnly = true)]
    [Description("Consulta partidas presupuestarias y órdenes de compra bloqueadas de una obra. Consulta hasta 10 páginas de 50 órdenes bloqueadas. Si alcanza ese límite y quedan órdenes pendientes, la respuesta indica que está incompleta. El filtro opcional de etapa se aplica a partidas y órdenes; estas últimas se filtran tras recuperar las páginas. Informa totales y paginación. Los importes de las órdenes son los datos oficiales de Recepción y no se recalculan. Solo lectura. Disponible para Comprador y Admin.")]
    public async Task<string> ConsultarPartidasPresupuestarias(
        [Description("Identificador GUID de la obra.")] string obraId,
        [Description("Nombre de etapa opcional para filtrar, por ejemplo: Estructura.")] string etapa = "",
        CancellationToken cancellationToken = default)
    {
        var resultado = await _presupuestos.ConsultarAsync(obraId, etapa, cancellationToken);
        return resultado.Datos.ToJsonString();
    }
}
