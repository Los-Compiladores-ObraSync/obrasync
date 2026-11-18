using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Api.Mcp;

/// <summary>Un material pedido: nombre (o código) del insumo y cantidad.</summary>
public sealed record ItemPedidoMcp(string Insumo, double Cantidad);

/// <summary>
/// Las herramientas del asistente, expuestas por MCP (tarjeta #98).
/// No contienen lógica propia: llaman a las MISMAS herramientas (HerramientasCatalogo) que usa el agente del chat,
/// así se escriben una sola vez y siguen las mismas reglas.
///  - Son de SOLO LECTURA: ninguna crea ni modifica datos. "armar_borrador_requisicion" solo arma un borrador.
///  - Corren "en nombre del usuario": consultan al Gateway con el JWT que llegó en el pedido MCP
///    (nunca una cuenta propia), así cada cliente ve solo lo que su rol permite.
/// </summary>
[McpServerToolType]
public sealed class HerramientasMcp
{
    private readonly HerramientasCatalogo _herramientas;
    private readonly HerramientasPresupuestos _presupuestos;

    public HerramientasMcp(HerramientasCatalogo herramientas, HerramientasPresupuestos presupuestos)
    {
        _herramientas = herramientas;
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

    [McpServerTool(Name = HerramientasCatalogo.BuscarInsumos, ReadOnly = true)]
    [Description("Busca insumos de construcción en el catálogo de ObraSync por texto (nombre o código). Solo lectura.")]
    public async Task<string> BuscarInsumos(
        [Description("Palabra a buscar, por ejemplo: cemento")] string texto,
        CancellationToken cancellationToken = default)
    {
        var argumentos = new JsonObject { ["texto"] = texto };
        var resultado = await _herramientas.EjecutarAsync(HerramientasCatalogo.BuscarInsumos, argumentos.ToJsonString(), cancellationToken);
        return resultado.Datos.ToJsonString();
    }

    [McpServerTool(Name = HerramientasCatalogo.ArmarBorrador, ReadOnly = true)]
    [Description(
        "Arma un BORRADOR de requisición de materiales para que la persona lo revise y confirme. No crea nada. " +
        "Resuelve la obra, la etapa y los insumos a partir de sus nombres. Si falta o es ambiguo algún dato, " +
        "devuelve un error con opciones para preguntarle a la persona. Solo lectura.")]
    public async Task<string> ArmarBorradorRequisicion(
        [Description("Nombre de la obra.")] string obra,
        [Description("Materiales pedidos (máximo 10): cada uno con el nombre o código del insumo y la cantidad (mayor a cero).")]
        ItemPedidoMcp[] items,
        [Description("Nombre de la etapa de la obra, por ejemplo: Estructura. Obligatoria si la obra tiene etapas.")]
        string etapa = "",
        [Description("Nota opcional de la persona.")] string observaciones = "",
        CancellationToken cancellationToken = default)
    {
        var lista = new JsonArray();
        foreach (var item in items ?? Array.Empty<ItemPedidoMcp>())
            lista.Add(new JsonObject { ["insumo"] = item.Insumo, ["cantidad"] = item.Cantidad });

        var argumentos = new JsonObject
        {
            ["obra"] = obra,
            // En el esquema MCP son "string" comunes (no "string o null", que algunos clientes de IA no soportan):
            // un texto vacío significa "no se indicó".
            ["etapa"] = string.IsNullOrWhiteSpace(etapa) ? null : etapa,
            ["observaciones"] = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones,
            ["items"] = lista
        };
        var resultado = await _herramientas.EjecutarAsync(HerramientasCatalogo.ArmarBorrador, argumentos.ToJsonString(), cancellationToken);
        return resultado.Datos.ToJsonString();
    }
}
