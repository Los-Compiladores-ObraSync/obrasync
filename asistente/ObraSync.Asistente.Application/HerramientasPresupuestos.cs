using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ObraSync.Asistente.Application;

/// <summary>Consulta de solo lectura de partidas y órdenes bloqueadas de una obra.</summary>
public sealed class HerramientasPresupuestos : IConsultaPresupuestoObra
{
    public const string ConsultarPartidas = "consultar_partidas_presupuestarias";
    public const int MaxPaginasOrdenesBloqueadas = 10;
    public const int TamanoPaginaOrdenesBloqueadas = 50;
    public static readonly TimeSpan TiempoTotalConsulta = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions OpcionesJsonWeb = new(JsonSerializerDefaults.Web);
    private static readonly Regex EspaciosMultiplesRegex = new(@"\s+", RegexOptions.Compiled);
    private readonly IConsultorPresupuestos _presupuestos;
    private readonly IConsultorOrdenesBloqueadas _ordenesBloqueadas;

    public HerramientasPresupuestos(
        IConsultorPresupuestos presupuestos,
        IConsultorOrdenesBloqueadas ordenesBloqueadas)
    {
        _presupuestos = presupuestos;
        _ordenesBloqueadas = ordenesBloqueadas;
    }

    public async Task<ResultadoHerramienta> ConsultarAsync(
        string obraIdTexto,
        string? etapa,
        CancellationToken ct)
    {
        if (!Guid.TryParse(obraIdTexto, out var obraId) || obraId == Guid.Empty)
            return ResultadoHerramienta.Error("argumentos_invalidos");

        var resultado = await _presupuestos.ListarPartidasAsync(
            obraId,
            string.IsNullOrWhiteSpace(etapa) ? null : etapa,
            ct);
        if (resultado.Error is not null)
        {
            return new ResultadoHerramienta(false, new JsonObject
            {
                ["ok"] = false,
                ["completo"] = false,
                ["error"] = resultado.Error
            });
        }

        var partidas = JsonSerializer.SerializeToNode(resultado.Partidas, OpcionesJsonWeb)?.AsArray() ?? new JsonArray();
        var totalPartidas = resultado.Total ?? partidas.Count;
        var partidasCompletas = resultado.Total.HasValue && totalPartidas == partidas.Count;
        var errorPartidas = !resultado.Total.HasValue
            ? "total_partidas_no_disponible"
            : partidasCompletas ? null : "total_partidas_no_coincide";
        var etapaFiltro = string.IsNullOrWhiteSpace(etapa) ? null : etapa.Trim();
        var etapaNormalizada = NormalizarEtapa(etapaFiltro);

        var ordenes = new List<OrdenCompraBloqueadaAsistente>();
        var idsOrdenes = new HashSet<Guid>();
        int? totalOrdenesApi = null;
        var paginasConsultadas = 0;
        var errorOrdenes = (string?)null;
        var ordenesCompletas = false;
        using (var limite = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            limite.CancelAfter(TiempoTotalConsulta);
            try
            {
                for (var pagina = 1; pagina <= MaxPaginasOrdenesBloqueadas; pagina++)
                {
                    var respuesta = await _ordenesBloqueadas.ListarAsync(
                        obraId, pagina, TamanoPaginaOrdenesBloqueadas, limite.Token);
                    paginasConsultadas++;
                    if (respuesta.Error is not null)
                    {
                        errorOrdenes = respuesta.Error;
                        break;
                    }

                    if (respuesta.Total is null ||
                        (totalOrdenesApi.HasValue && totalOrdenesApi.Value != respuesta.Total.Value))
                    {
                        errorOrdenes = "total_cambio_durante_consulta";
                        break;
                    }

                    totalOrdenesApi ??= respuesta.Total;
                    var paginaOrdenes = respuesta.Ordenes ?? Array.Empty<OrdenCompraBloqueadaAsistente>();
                    if (paginaOrdenes.Count > TamanoPaginaOrdenesBloqueadas)
                    {
                        errorOrdenes = "respuesta_invalida";
                        break;
                    }
                    if (paginaOrdenes.Any(o => !idsOrdenes.Add(o.OrdenCompraId)))
                    {
                        errorOrdenes = "datos_cambiaron_durante_consulta";
                        break;
                    }
                    ordenes.AddRange(paginaOrdenes);

                    if (ordenes.Count > totalOrdenesApi.Value)
                    {
                        errorOrdenes = "respuesta_invalida";
                        break;
                    }

                    if (ordenes.Count == totalOrdenesApi.Value)
                    {
                        ordenesCompletas = true;
                        break;
                    }

                    if (paginaOrdenes.Count == 0)
                    {
                        errorOrdenes = "respuesta_incompleta";
                        break;
                    }

                    if (pagina == MaxPaginasOrdenesBloqueadas)
                        errorOrdenes = "limite_paginas_alcanzado";
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                errorOrdenes = "api_timeout";
            }
        }

        if (ct.IsCancellationRequested)
            ct.ThrowIfCancellationRequested();

        var ordenesFiltradas = etapaNormalizada is null
            ? ordenes
            : ordenes.Where(o => string.Equals(
                NormalizarEtapa(o.Etapa), etapaNormalizada, StringComparison.Ordinal)).ToList();
        var consultaCompleta = partidasCompletas && ordenesCompletas;
        var datos = new JsonObject
        {
            ["ok"] = true,
            ["completo"] = consultaCompleta,
            ["error"] = errorOrdenes ?? errorPartidas,
            ["partidas"] = partidas,
            ["total"] = totalPartidas,
            ["mostradas"] = partidas.Count,
            ["hayMas"] = resultado.Total.HasValue && totalPartidas > partidas.Count,
            ["hayMasConocido"] = resultado.Total.HasValue,
            ["partidasCompletas"] = partidasCompletas,
            ["partidasTotalConocido"] = resultado.Total.HasValue,
            ["ordenesBloqueadas"] = JsonSerializer.SerializeToNode(ordenesFiltradas, OpcionesJsonWeb)?.AsArray() ?? new JsonArray(),
            ["ordenesBloqueadasPaginacion"] = new JsonObject
            {
                ["totalApi"] = totalOrdenesApi,
                ["totalFiltrado"] = ordenesCompletas ? ordenesFiltradas.Count : null,
                ["mostradas"] = ordenesFiltradas.Count,
                ["paginasConsultadas"] = paginasConsultadas,
                ["maxPaginas"] = MaxPaginasOrdenesBloqueadas,
                ["tamanoPagina"] = TamanoPaginaOrdenesBloqueadas,
                ["maxResultados"] = MaxPaginasOrdenesBloqueadas * TamanoPaginaOrdenesBloqueadas,
                ["completo"] = ordenesCompletas,
                ["hayMas"] = ordenesCompletas
                    ? false
                    : totalOrdenesApi.HasValue
                        ? ordenes.Count < totalOrdenesApi.Value
                        : null,
                ["filtroEtapa"] = etapaFiltro,
                ["error"] = errorOrdenes
            }
        };

        return new ResultadoHerramienta(true, datos);
    }

    // Mantiene los mismos criterios de NormalizadorEtapa de Recepción (#196), sin añadir
    // parámetros al endpoint de órdenes bloqueadas.
    private static string? NormalizarEtapa(string? etapa)
    {
        if (string.IsNullOrWhiteSpace(etapa))
            return null;

        var limpio = EspaciosMultiplesRegex.Replace(etapa.Trim(), " ").ToLowerInvariant();
        var descompuesto = limpio.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
