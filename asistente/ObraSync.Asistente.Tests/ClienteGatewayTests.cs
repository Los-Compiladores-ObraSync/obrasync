using System.Text.Json.Nodes;
using ObraSync.Asistente.Application;
using ObraSync.Asistente.Infrastructure;

namespace ObraSync.Asistente.Tests;

public class ClienteGatewayTests
{
    private static readonly ConfiguracionIA Config =
        new("https://ejemplo.test/unaj-llm", "gpt-4.1-mini", "2025-03-01-preview", "CLAVE_DE_PRUEBA");

    private static JsonArray Mensajes() => new(new JsonObject { ["role"] = "user", ["content"] = "hola" });

    [Fact]
    public async Task Manda_la_clave_en_el_encabezado_api_key_y_arma_la_url_con_deployment_y_api_version()
    {
        var handler = new HandlerModelo();
        var cliente = new ClienteGateway(new HttpClient(handler), Config);

        await cliente.PedirAsync(Mensajes(), null, 300, default);

        Assert.Equal("CLAVE_DE_PRUEBA", handler.ApiKey);
        Assert.Contains("/openai/deployments/gpt-4.1-mini/chat/completions?api-version=2025-03-01-preview", handler.Url);
    }

    [Fact]
    public async Task Manda_max_completion_tokens_y_las_herramientas_cuando_hay()
    {
        var handler = new HandlerModelo();
        var cliente = new ClienteGateway(new HttpClient(handler), Config);

        await cliente.PedirAsync(Mensajes(), HerramientasCatalogo.Definiciones(), 300, default);

        var cuerpo = JsonNode.Parse(handler.Cuerpo!)!;
        Assert.Equal(300, cuerpo["max_completion_tokens"]!.GetValue<int>());
        Assert.Equal("auto", cuerpo["tool_choice"]!.GetValue<string>());
        Assert.Equal("buscar_insumos", cuerpo["tools"]![0]!["function"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Parsea_el_contenido_y_los_tokens_de_la_respuesta()
    {
        var cliente = new ClienteGateway(new HttpClient(new HandlerModelo()), Config);

        var r = await cliente.PedirAsync(Mensajes(), null, 300, default);

        Assert.Equal("¡Hola!", r.Mensaje["content"]!.GetValue<string>());
        Assert.Equal(16, r.TokensTotales);
    }

    [Fact]
    public void La_configuracion_no_muestra_la_clave_al_imprimirse()
    {
        Assert.DoesNotContain("CLAVE_DE_PRUEBA", Config.ToString());
    }

    [Theory]
    [InlineData(401, "modelo_clave_invalida")]
    [InlineData(403, "modelo_sin_permiso_o_cupo")]
    [InlineData(404, "modelo_no_encontrado")]
    [InlineData(429, "modelo_limite_de_tasa")]
    [InlineData(500, "modelo_error_http")]
    public async Task Los_codigos_HTTP_del_proveedor_se_traducen_a_errores_estables(int estado, string esperado)
    {
        var cliente = new ClienteGateway(new HttpClient(new HandlerModelo(estado)), Config);

        var ex = await Assert.ThrowsAsync<ModeloException>(() => cliente.PedirAsync(Mensajes(), null, 10, default));

        Assert.Equal(esperado, ex.Codigo);
    }

    [Fact]
    public async Task Sin_clave_el_asistente_esta_apagado_y_no_se_llama_al_proveedor()
    {
        var handler = new HandlerModelo();
        var sinClave = new ConfiguracionIA("https://ejemplo.test/unaj-llm", null, null, "");
        var cliente = new ClienteGateway(new HttpClient(handler), sinClave);

        var ex = await Assert.ThrowsAsync<ModeloException>(() => cliente.PedirAsync(Mensajes(), null, 10, default));

        Assert.Equal("asistente_apagado", ex.Codigo);
        Assert.Equal(0, handler.Llamadas);
    }

    [Fact]
    public void La_configuracion_usa_valores_por_defecto_para_deployment_y_api_version()
    {
        var c = new ConfiguracionIA("https://ejemplo.test/x/", null, null, "k");

        Assert.Equal("gpt-4.1-mini", c.Deployment);
        Assert.Equal("2025-03-01-preview", c.ApiVersion);
        Assert.Equal("https://ejemplo.test/x", c.BaseUrl);
        Assert.True(c.EstaActivo);
    }
}
