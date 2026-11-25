using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Tests;

/// <summary>listar_mis_obras: el Capataz puede preguntar qué obras tiene asignadas si no se acuerda del nombre.</summary>
public class HerramientasObrasTests
{
    private static Agente ArmarAgente(IModeloChat modelo, string? rol, ObrasFalsas obras) => new(
        modelo,
        new HerramientasCatalogo(new BuscadorFalso(), obras),
        new HerramientasCompras(new ConsultorFalso(), obras),
        new ContextoFalso { Rol = rol });

    [Fact]
    public async Task Lista_cada_obra_con_sus_etapas()
    {
        var herramientas = new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas());

        var r = await herramientas.EjecutarAsync(HerramientasCatalogo.ListarObras, "{}", default);

        Assert.True(r.Ok);
        Assert.Equal(2, r.Datos["total"]!.GetValue<int>());
        Assert.False(r.Datos["hayMas"]!.GetValue<bool>());
        var obras = r.Datos["obras"]!.AsArray();
        Assert.Equal("Torre Central", obras[0]!["nombre"]!.GetValue<string>());
        Assert.Equal(new[] { "Estructura", "Obra Gruesa" }, obras[0]!["etapas"]!.AsArray().Select(e => e!.GetValue<string>()));
        Assert.Empty(obras[1]!["etapas"]!.AsArray());
    }

    [Fact]
    public async Task No_devuelve_ids_de_obra_porque_el_modelo_nunca_ve_ni_inventa_ids()
    {
        var herramientas = new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas());

        var r = await herramientas.EjecutarAsync(HerramientasCatalogo.ListarObras, "{}", default);

        Assert.DoesNotContain(ObrasFalsas.IdTorre.ToString(), r.Datos.ToJsonString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Acepta_argumentos_vacios(string argumentos)
    {
        var herramientas = new HerramientasCatalogo(new BuscadorFalso(), new ObrasFalsas());

        var r = await herramientas.EjecutarAsync(HerramientasCatalogo.ListarObras, argumentos, default);

        Assert.True(r.Ok);
    }

    [Theory]
    [InlineData("esto no es json")]
    [InlineData("[1,2]")]
    public async Task Argumentos_que_no_son_un_objeto_se_rechazan_sin_llamar_a_la_API(string argumentos)
    {
        var obras = new ObrasFalsas();
        var herramientas = new HerramientasCatalogo(new BuscadorFalso(), obras);

        var r = await herramientas.EjecutarAsync(HerramientasCatalogo.ListarObras, argumentos, default);

        Assert.False(r.Ok);
        Assert.Contains("argumentos_invalidos", r.Datos.ToJsonString());
        Assert.Equal(0, obras.Llamadas);
    }

    [Fact]
    public async Task Si_hay_mas_obras_que_el_tope_lo_avisa_y_no_las_devuelve_todas()
    {
        var obras = new ObrasFalsas
        {
            Resultado = ResultadoObras.Bien(Enumerable.Range(1, HerramientasCatalogo.MaxObrasListadas + 3)
                .Select(i => new ObraAsistente(Guid.NewGuid(), $"Obra {i}", new[] { "Estructura" }))
                .ToList())
        };
        var herramientas = new HerramientasCatalogo(new BuscadorFalso(), obras);

        var r = await herramientas.EjecutarAsync(HerramientasCatalogo.ListarObras, "{}", default);

        Assert.True(r.Ok);
        Assert.Equal(HerramientasCatalogo.MaxObrasListadas + 3, r.Datos["total"]!.GetValue<int>());
        Assert.True(r.Datos["hayMas"]!.GetValue<bool>());
        Assert.Equal(HerramientasCatalogo.MaxObrasListadas, r.Datos["obras"]!.AsArray().Count);
    }

    [Fact]
    public async Task Sin_obras_asignadas_devuelve_una_lista_vacia_y_ok()
    {
        var obras = new ObrasFalsas { Resultado = ResultadoObras.Bien(new List<ObraAsistente>()) };
        var herramientas = new HerramientasCatalogo(new BuscadorFalso(), obras);

        var r = await herramientas.EjecutarAsync(HerramientasCatalogo.ListarObras, "{}", default);

        Assert.True(r.Ok);
        Assert.Equal(0, r.Datos["total"]!.GetValue<int>());
        Assert.Empty(r.Datos["obras"]!.AsArray());
    }

    [Fact]
    public async Task Si_la_API_falla_devuelve_el_codigo_de_error_estable()
    {
        var obras = new ObrasFalsas { Resultado = ResultadoObras.Falla("sin_permiso") };
        var herramientas = new HerramientasCatalogo(new BuscadorFalso(), obras);

        var r = await herramientas.EjecutarAsync(HerramientasCatalogo.ListarObras, "{}", default);

        Assert.False(r.Ok);
        Assert.Contains("sin_permiso", r.Datos.ToJsonString());
    }

    [Fact]
    public async Task El_Capataz_pregunta_que_obras_hay_y_el_modelo_recibe_la_lista()
    {
        var obras = new ObrasFalsas();
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta("{}", nombre: HerramientasCatalogo.ListarObras),
            _ => Respuestas.Final("Tenés Torre Central y Galpón Norte."));

        var r = await ArmarAgente(modelo, "Capataz", obras).ResponderAsync("¿qué obras hay?");

        Assert.True(r.Ok);
        Assert.Equal(1, obras.Llamadas);
        Assert.Contains("Torre Central", modelo.Pedidos[1].Last()!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task El_Comprador_no_la_tiene_en_su_perfil_y_no_se_consulta_nada()
    {
        var obras = new ObrasFalsas();
        var modelo = new ModeloFalso(
            _ => Respuestas.PideHerramienta("{}", nombre: HerramientasCatalogo.ListarObras),
            _ => Respuestas.Final("No puedo."));

        await ArmarAgente(modelo, "Comprador", obras).ResponderAsync("¿qué obras hay?");

        Assert.Equal(0, obras.Llamadas);
        Assert.Contains("herramienta_no_permitida", modelo.Pedidos[1].Last()!["content"]!.GetValue<string>());
    }

    [Fact]
    public void Es_de_solo_lectura_y_las_instrucciones_del_Capataz_la_mencionan()
    {
        Assert.Contains(HerramientasCatalogo.ListarObras, PerfilAsistente.Para("Capataz")!.Herramientas);
        Assert.Contains(HerramientasCatalogo.ListarObras, PerfilAsistente.Para("Admin")!.Herramientas);
        Assert.DoesNotContain(HerramientasCatalogo.ListarObras, PerfilAsistente.Para("Comprador")!.Herramientas);
        Assert.Contains("listar_mis_obras", PerfilAsistente.Para("Capataz")!.Instrucciones);
    }
}
