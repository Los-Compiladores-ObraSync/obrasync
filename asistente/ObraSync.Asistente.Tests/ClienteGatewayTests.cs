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

    private static readonly ConfiguracionIA ConfigV1 = new(
        "https://ejemplo.test/default/models/openai/v1", "shared-unaj-newsku-resource/gpt-5-mini", null, "CLAVE_V1");

    [Fact]
    public async Task Con_endpoint_openai_v1_usa_Bearer_pone_el_modelo_en_el_cuerpo_y_no_usa_api_version()
    {
        var handler = new HandlerModelo();
        var cliente = new ClienteGateway(new HttpClient(handler), ConfigV1);

        await cliente.PedirAsync(Mensajes(), null, 300, default);

        Assert.Equal("https://ejemplo.test/default/models/openai/v1/chat/completions", handler.Url);
        Assert.Equal("Bearer CLAVE_V1", handler.Authorization);
        Assert.Null(handler.ApiKey);
        var cuerpo = JsonNode.Parse(handler.Cuerpo!)!;
        Assert.Equal("shared-unaj-newsku-resource/gpt-5-mini", cuerpo["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task Con_gpt_5_manda_reasoning_effort_minimal_para_que_pensar_no_se_coma_la_respuesta()
    {
        var handler = new HandlerModelo();
        var cliente = new ClienteGateway(new HttpClient(handler), ConfigV1);

        await cliente.PedirAsync(Mensajes(), null, 300, default);

        Assert.Equal("minimal", JsonNode.Parse(handler.Cuerpo!)!["reasoning_effort"]!.GetValue<string>());
    }

    [Fact]
    public async Task Con_gpt_4_1_no_manda_reasoning_effort_ni_model_en_el_estilo_azure()
    {
        var handler = new HandlerModelo();
        var cliente = new ClienteGateway(new HttpClient(handler), Config);

        await cliente.PedirAsync(Mensajes(), null, 300, default);

        var cuerpo = JsonNode.Parse(handler.Cuerpo!)!;
        Assert.Null(cuerpo["reasoning_effort"]);
        Assert.Null(cuerpo["model"]);
        Assert.Null(handler.Authorization);
    }

    [Fact]
    public async Task El_reasoning_effort_se_puede_cambiar_o_apagar_por_configuracion()
    {
        var handler = new HandlerModelo();
        var baja = new ConfiguracionIA(ConfigV1.BaseUrl, ConfigV1.Deployment, null, "k", null, "low");
        await new ClienteGateway(new HttpClient(handler), baja).PedirAsync(Mensajes(), null, 300, default);
        Assert.Equal("low", JsonNode.Parse(handler.Cuerpo!)!["reasoning_effort"]!.GetValue<string>());

        var apagado = new ConfiguracionIA(ConfigV1.BaseUrl, ConfigV1.Deployment, null, "k", null, "none");
        await new ClienteGateway(new HttpClient(handler), apagado).PedirAsync(Mensajes(), null, 300, default);
        Assert.Null(JsonNode.Parse(handler.Cuerpo!)!["reasoning_effort"]);
    }

    [Fact]
    public void Un_reasoning_effort_vacio_usa_el_valor_por_defecto_como_pasa_con_las_variables_opcionales_del_compose()
    {
        Assert.Equal("minimal", new ConfiguracionIA(ConfigV1.BaseUrl, ConfigV1.Deployment, null, "k", null, "").ReasoningEffort);
        Assert.Equal("", new ConfiguracionIA(Config.BaseUrl, "gpt-4.1-mini", null, "k", null, "  ").ReasoningEffort);
    }

    [Theory]
    [InlineData("https://ejemplo.test/default/models/openai/v1", true)]
    [InlineData("https://ejemplo.test/default/models/openai/v1/", true)]
    [InlineData("https://ejemplo.test/unaj-llm", false)]
    public void El_estilo_se_detecta_por_el_final_de_la_url(string url, bool esperado)
    {
        Assert.Equal(esperado, new ConfiguracionIA(url, null, null, "k").EstiloOpenAiV1);
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
