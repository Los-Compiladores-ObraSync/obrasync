using System.Text.Json.Nodes;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Tests;

/// <summary>Herramientas de recepción del Capataz (#147): validan antes de llamar a la API y nunca registran nada.</summary>
public class HerramientasRecepcionTests
{
    private static (HerramientasRecepcion Herramientas, ConsultorRecepcionesFalso Consultor) Armar()
    {
        var consultor = new ConsultorRecepcionesFalso();
        return (new HerramientasRecepcion(consultor, new ObrasFalsas()), consultor);
    }

    private static JsonObject Datos(ResultadoHerramienta r) => (JsonObject)r.Datos;

    private static string Args(string orden, string remito, string? observaciones = null) =>
        new JsonObject { ["ordenCompraId"] = orden, ["numeroRemito"] = remito, ["observaciones"] = observaciones }.ToJsonString();

    [Fact]
    public void Las_definiciones_son_dos_y_el_remito_es_obligatorio()
    {
        var defs = HerramientasRecepcion.Definiciones();

        Assert.Equal(new[] { "listar_ordenes_pendientes", "preparar_recepcion" },
            defs.Select(d => d!["function"]!["name"]!.GetValue<string>()));
        var requeridos = defs[1]!["function"]!["parameters"]!["required"]!.AsArray().Select(n => n!.GetValue<string>());
        Assert.Equal(new[] { "ordenCompraId", "numeroRemito" }, requeridos);
    }

    [Fact]
    public async Task Listar_devuelve_la_orden_con_obra_etapa_y_materiales()
    {
        var (h, consultor) = Armar();

        var r = await h.EjecutarAsync(HerramientasRecepcion.ListarOrdenes, "{}", default);

        Assert.True(r.Ok);
        var datos = Datos(r);
        Assert.Equal(1, datos["total"]!.GetValue<int>());
        Assert.False(datos["hayMas"]!.GetValue<bool>());
        var orden = datos["ordenes"]![0]!;
        Assert.Equal(ConsultorRecepcionesFalso.IdOrden.ToString(), orden["ordenCompraId"]!.GetValue<string>());
        Assert.Equal(1, orden["opcion"]!.GetValue<int>());
        Assert.Equal("Torre Central", orden["obra"]!.GetValue<string>());
        Assert.Equal("Estructura", orden["etapa"]!.GetValue<string>());
        Assert.Equal(2, orden["items"]!.AsArray().Count);
        Assert.Equal(HerramientasRecepcion.MaxOrdenes, consultor.UltimoMaximo);
    }

    [Fact]
    public async Task Listar_avisa_si_hay_mas_ordenes_que_las_mostradas()
    {
        var (h, consultor) = Armar();
        consultor.Resultado = ResultadoOrdenesPendientes.Bien(new[] { ConsultorRecepcionesFalso.Orden }, 9);

        var r = await h.EjecutarAsync(HerramientasRecepcion.ListarOrdenes, "{}", default);

        Assert.True(Datos(r)["hayMas"]!.GetValue<bool>());
        Assert.Equal(9, Datos(r)["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task Listar_propaga_el_error_estable_de_la_API()
    {
        var (h, consultor) = Armar();
        consultor.Resultado = ResultadoOrdenesPendientes.Falla("sin_permiso");

        var r = await h.EjecutarAsync(HerramientasRecepcion.ListarOrdenes, "{}", default);

        Assert.False(r.Ok);
        Assert.Equal("sin_permiso", Datos(r)["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Preparar_arma_un_borrador_de_recepcion_total_y_el_modelo_solo_ve_un_resumen()
    {
        var (h, _) = Armar();

        var r = await h.EjecutarAsync(
            HerramientasRecepcion.PrepararRecepcion, Args(ConsultorRecepcionesFalso.IdOrden.ToString(), " R-0001-123 ", "Todo en orden"), default);

        Assert.True(r.Ok);
        var b = r.Recepcion!;
        Assert.Equal("registrarRecepcion", b.Accion);
        Assert.Equal("R-0001-123", b.Recepcion.NumeroRemito);
        Assert.Equal("Todo en orden", b.Recepcion.Observaciones);
        Assert.Equal(ObrasFalsas.IdTorre, b.Recepcion.ObraId);
        Assert.Equal(ConsultorRecepcionesFalso.IdOrden, b.Recepcion.OrdenCompraId);
        Assert.Equal(2, b.Recepcion.Detalles.Count);
        // Recepción TOTAL: lo recibido es igual a lo esperado y está conforme.
        Assert.All(b.Recepcion.Detalles, d =>
        {
            Assert.Equal(d.CantidadEsperada, d.CantidadRecibida);
            Assert.True(d.Conforme);
        });
        Assert.Equal("Torre Central", b.Resumen.Obra);
        Assert.Equal(2, b.Resumen.Items.Count);
        // El modelo no recibe el borrador entero.
        Assert.True(Datos(r)["borradorListo"]!.GetValue<bool>());
        Assert.Null(Datos(r)["ordenCompraId"]);
    }

    [Fact]
    public async Task Preparar_sin_observaciones_deja_el_campo_en_null()
    {
        var (h, _) = Armar();

        var r = await h.EjecutarAsync(
            HerramientasRecepcion.PrepararRecepcion, Args(ConsultorRecepcionesFalso.IdOrden.ToString(), "R-1", "   "), default);

        Assert.True(r.Ok);
        Assert.Null(r.Recepcion!.Recepcion.Observaciones);
    }

    [Theory]
    [InlineData("no-es-un-guid", "R-1")]
    [InlineData("00000000-0000-0000-0000-000000000000", "R-1")]
    [InlineData("cccccccc-0000-0000-0000-000000000001", "")]
    [InlineData("cccccccc-0000-0000-0000-000000000001", "   ")]
    public async Task Preparar_con_argumentos_invalidos_no_llama_a_la_API(string orden, string remito)
    {
        var (h, consultor) = Armar();

        var r = await h.EjecutarAsync(HerramientasRecepcion.PrepararRecepcion, Args(orden, remito), default);

        Assert.False(r.Ok);
        Assert.Equal("argumentos_invalidos", Datos(r)["error"]!.GetValue<string>());
        Assert.Null(r.Recepcion);
        Assert.Equal(0, consultor.Llamadas);
    }

    [Fact]
    public async Task Preparar_rechaza_un_remito_o_unas_observaciones_demasiado_largos()
    {
        var (h, consultor) = Armar();
        var id = ConsultorRecepcionesFalso.IdOrden.ToString();

        var remito = await h.EjecutarAsync(HerramientasRecepcion.PrepararRecepcion, Args(id, new string('x', 51)), default);
        var obs = await h.EjecutarAsync(HerramientasRecepcion.PrepararRecepcion, Args(id, "R-1", new string('x', 501)), default);

        Assert.Equal("argumentos_invalidos", Datos(remito)["error"]!.GetValue<string>());
        Assert.Equal("argumentos_invalidos", Datos(obs)["error"]!.GetValue<string>());
        Assert.Equal(0, consultor.Llamadas);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("no es json")]
    public async Task Preparar_con_argumentos_que_no_son_un_objeto_json_falla_sin_llamar_a_la_API(string argumentos)
    {
        var (h, consultor) = Armar();

        var r = await h.EjecutarAsync(HerramientasRecepcion.PrepararRecepcion, argumentos, default);

        Assert.False(r.Ok);
        Assert.Equal(0, consultor.Llamadas);
    }

    [Fact]
    public async Task Preparar_una_orden_que_no_esta_pendiente_devuelve_orden_no_encontrada()
    {
        var (h, _) = Armar();

        var r = await h.EjecutarAsync(
            HerramientasRecepcion.PrepararRecepcion, Args("eeeeeeee-0000-0000-0000-000000000009", "R-1"), default);

        Assert.False(r.Ok);
        Assert.Equal("orden_no_encontrada", Datos(r)["error"]!.GetValue<string>());
        Assert.Null(r.Recepcion);
    }

    [Fact]
    public async Task Preparar_vuelve_a_leer_las_ordenes_en_la_API_con_la_pagina_mas_grande()
    {
        var (h, consultor) = Armar();

        await h.EjecutarAsync(HerramientasRecepcion.PrepararRecepcion, Args(ConsultorRecepcionesFalso.IdOrden.ToString(), "R-1"), default);

        Assert.Equal(1, consultor.Llamadas);
        Assert.Equal(HerramientasRecepcion.MaxBusqueda, consultor.UltimoMaximo);
    }

    [Fact]
    public async Task Una_herramienta_desconocida_devuelve_herramienta_desconocida()
    {
        var (h, _) = Armar();

        var r = await h.EjecutarAsync("registrar_recepcion", "{}", default);

        Assert.False(r.Ok);
        Assert.Equal("herramienta_desconocida", Datos(r)["error"]!.GetValue<string>());
    }
}
