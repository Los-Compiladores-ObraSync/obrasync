using NSubstitute;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Tests;

public class HerramientasCatalogoTests
{
    [Fact]
    public async Task Herramienta_desconocida_devuelve_error_estable()
    {
        var herramientas = new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas());

        var r = await herramientas.EjecutarAsync("borrar_todo", "{}", default);

        Assert.False(r.Ok);
        Assert.Contains("herramienta_desconocida", r.Datos.ToJsonString());
    }

    [Fact]
    public async Task Texto_demasiado_largo_se_rechaza_sin_llamar_a_la_API()
    {
        var buscador = Substitute.For<IBuscadorInsumos>();
        var herramientas = new HerramientasCatalogo(buscador, new ObrasFalsas());

        var r = await herramientas.EjecutarAsync(
            "buscar_insumos", "{\"texto\":\"" + new string('a', HerramientasCatalogo.MaxTextoBusqueda + 1) + "\"}", default);

        Assert.False(r.Ok);
        Assert.Empty(buscador.ReceivedCalls());
    }

    [Theory]
    [InlineData("esto no es json")]
    [InlineData("{}")]
    [InlineData("{\"texto\":\"   \"}")]
    [InlineData("{\"texto\":123}")]
    public async Task Argumentos_invalidos_se_rechazan_sin_llamar_a_la_API(string argumentos)
    {
        var buscador = Substitute.For<IBuscadorInsumos>();
        var herramientas = new HerramientasCatalogo(buscador, new ObrasFalsas());

        var r = await herramientas.EjecutarAsync("buscar_insumos", argumentos, default);

        Assert.False(r.Ok);
        Assert.Contains("argumentos_invalidos", r.Datos.ToJsonString());
        Assert.Empty(buscador.ReceivedCalls());
    }

    [Fact]
    public async Task Busqueda_valida_devuelve_los_insumos_con_los_campos_que_el_modelo_necesita()
    {
        var herramientas = new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas());

        var r = await herramientas.EjecutarAsync("buscar_insumos", "{\"texto\":\"cemento\"}", default);

        Assert.True(r.Ok);
        var primero = r.Datos["insumos"]![0]!;
        Assert.Equal("MAT-CEM-001", primero["codigo"]!.GetValue<string>());
        Assert.Equal("BOLSA", primero["unidadMedida"]!.GetValue<string>());
    }

    [Fact]
    public void Las_herramientas_expuestas_al_modelo_son_solo_de_lectura_y_borrador()
    {
        var nombres = HerramientasCatalogo.Definiciones()
            .Select(d => d!["function"]!["name"]!.GetValue<string>())
            .ToArray();

        // Ninguna escribe datos: armar_borrador_requisicion solo devuelve un borrador (ver #100).
        Assert.Equal(new[] { "buscar_insumos", "listar_mis_obras", "armar_borrador_requisicion" }, nombres);
    }
}
