using ObraSync.Asistente.Application;
using ObraSync.Asistente.Infrastructure;

namespace ObraSync.Asistente.Tests;

/// <summary>Configuración y arranque del lector de remitos (#209). No llama a Azure.</summary>
public class LectorRemitoAzureTests
{
    [Fact]
    public void El_deployment_de_remitos_es_el_del_chat_si_no_se_define_otro()
    {
        var c = new ConfiguracionIA("https://ejemplo.test/x", "gpt-4.1-mini", null, "k");

        Assert.Equal("gpt-4.1-mini", c.DeploymentRemito);
    }

    [Fact]
    public void Se_puede_usar_un_modelo_mas_potente_solo_para_remitos()
    {
        var c = new ConfiguracionIA("https://ejemplo.test/x", "gpt-4.1-mini", null, "k", "gpt-4.1");

        Assert.Equal("gpt-4.1", c.DeploymentRemito);
        Assert.Equal("gpt-4.1-mini", c.Deployment);
        Assert.DoesNotContain("k)", c.ToString());
    }

    [Fact]
    public async Task Sin_clave_el_lector_arranca_pero_responde_asistente_apagado()
    {
        var lector = new LectorRemitoAzure(new ConfiguracionIA("", null, null, ""));

        var ex = await Assert.ThrowsAsync<ModeloException>(() => lector.ExtraerAsync(new byte[] { 1 }, "image/jpeg", default));

        Assert.Equal("asistente_apagado", ex.Codigo);
    }

    [Fact]
    public void Con_clave_y_url_se_construye_sin_llamar_a_la_red()
    {
        var lector = new LectorRemitoAzure(new ConfiguracionIA("https://ejemplo.test/unaj-llm", null, null, "clave"));

        Assert.NotNull(lector);
    }
}
