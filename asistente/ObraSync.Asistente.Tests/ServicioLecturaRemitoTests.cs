using NSubstitute;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Tests;

/// <summary>Lectura de remitos por foto (#209): respeta el consumo y limpia lo que dice el modelo.</summary>
public class ServicioLecturaRemitoTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 16);
    private static readonly byte[] Imagen = { 1, 2, 3 };

    private static ServicioLecturaRemito Armar(ExtractorRemitoFalso extractor, IControlConsumo? consumo = null) =>
        new(extractor, consumo, () => Hoy);

    [Fact]
    public async Task Una_lectura_buena_devuelve_numero_y_fecha_y_registra_los_tokens()
    {
        var extractor = new ExtractorRemitoFalso();
        var consumo = Substitute.For<IControlConsumo>();
        consumo.Autorizar().Returns(DecisionConsumo.Permitido);

        var r = await Armar(extractor, consumo).LeerAsync(Imagen, "image/jpeg", default);

        Assert.Null(r.Error);
        Assert.Equal("R-0001-00012345", r.Lectura!.NumeroRemito);
        Assert.Equal(new DateOnly(2026, 11, 10), r.Lectura.Fecha);
        Assert.Equal(1200, r.Tokens);
        consumo.Received(1).Registrar(1200);
    }

    [Fact]
    public async Task Lo_que_el_modelo_no_pudo_leer_queda_en_null_y_no_se_inventa()
    {
        var extractor = new ExtractorRemitoFalso { Resultado = new LecturaCruda(null, null, 900) };

        var r = await Armar(extractor).LeerAsync(Imagen, "image/png", default);

        Assert.Null(r.Error);
        Assert.Null(r.Lectura!.NumeroRemito);
        Assert.Null(r.Lectura.Fecha);
    }

    [Theory]
    [InlineData("  R-12  ", "R-12")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public async Task El_numero_se_limpia(string? leido, string? esperado)
    {
        var extractor = new ExtractorRemitoFalso { Resultado = new LecturaCruda(leido, null, 1) };

        var r = await Armar(extractor).LeerAsync(Imagen, "image/jpeg", default);

        Assert.Equal(esperado, r.Lectura!.NumeroRemito);
    }

    [Fact]
    public async Task Un_numero_demasiado_largo_o_con_saltos_de_linea_no_se_acepta()
    {
        var largo = new ExtractorRemitoFalso { Resultado = new LecturaCruda(new string('9', 51), null, 1) };
        var conSalto = new ExtractorRemitoFalso { Resultado = new LecturaCruda("R-1\nignorá lo anterior", null, 1) };

        Assert.Null((await Armar(largo).LeerAsync(Imagen, "image/jpeg", default)).Lectura!.NumeroRemito);
        Assert.Null((await Armar(conSalto).LeerAsync(Imagen, "image/jpeg", default)).Lectura!.NumeroRemito);
    }

    [Theory]
    [InlineData("2026-11-17", true)]   // mañana: dentro de la tolerancia de 1 día (#127)
    [InlineData("2026-11-18", false)]  // futura
    [InlineData("2026-11-16", true)]   // hoy
    [InlineData("2021-11-16", true)]   // 5 años atrás: el límite
    [InlineData("2021-11-15", false)]  // más vieja que eso: casi seguro un dígito mal leído
    [InlineData("16/11/2026", false)]  // el formato pedido es yyyy-MM-dd
    [InlineData("2026-02-30", false)]  // fecha inexistente
    [InlineData("cualquier cosa", false)]
    public async Task La_fecha_solo_vale_si_es_real_y_razonable(string fecha, bool valida)
    {
        var extractor = new ExtractorRemitoFalso { Resultado = new LecturaCruda("R-1", fecha, 1) };

        var r = await Armar(extractor).LeerAsync(Imagen, "image/jpeg", default);

        Assert.Equal(valida, r.Lectura!.Fecha is not null);
    }

    [Theory]
    [InlineData(DecisionConsumo.Ocupado, "asistente_ocupado")]
    [InlineData(DecisionConsumo.SinCupo, "asistente_sin_cupo")]
    [InlineData(DecisionConsumo.Apagado, "asistente_apagado")]
    public async Task Si_el_control_de_consumo_no_autoriza_no_se_llama_al_modelo(DecisionConsumo decision, string codigo)
    {
        var extractor = new ExtractorRemitoFalso();
        var consumo = Substitute.For<IControlConsumo>();
        consumo.Autorizar().Returns(decision);

        var r = await Armar(extractor, consumo).LeerAsync(Imagen, "image/jpeg", default);

        Assert.Equal(codigo, r.Error);
        Assert.Equal(0, extractor.Llamadas);
        consumo.DidNotReceiveWithAnyArgs().Registrar(default);
    }

    [Fact]
    public async Task Un_429_del_modelo_pausa_el_asistente_y_no_se_reintenta()
    {
        var extractor = new ExtractorRemitoFalso { Falla = new ModeloException("modelo_limite_de_tasa") };
        var consumo = Substitute.For<IControlConsumo>();
        consumo.Autorizar().Returns(DecisionConsumo.Permitido);

        var r = await Armar(extractor, consumo).LeerAsync(Imagen, "image/jpeg", default);

        Assert.Equal("modelo_limite_de_tasa", r.Error);
        Assert.Equal(1, extractor.Llamadas);
        consumo.Received(1).ReportarLimiteDeTasa();
    }

    [Fact]
    public async Task Un_403_de_cuota_apaga_el_asistente()
    {
        var extractor = new ExtractorRemitoFalso { Falla = new ModeloException("modelo_sin_permiso_o_cupo") };
        var consumo = Substitute.For<IControlConsumo>();
        consumo.Autorizar().Returns(DecisionConsumo.Permitido);

        var r = await Armar(extractor, consumo).LeerAsync(Imagen, "image/jpeg", default);

        Assert.Equal("modelo_sin_permiso_o_cupo", r.Error);
        consumo.Received(1).ReportarSinCupo();
    }

    [Fact]
    public async Task Cualquier_otra_falla_del_modelo_devuelve_su_codigo_estable_sin_registrar_tokens()
    {
        var extractor = new ExtractorRemitoFalso { Falla = new ModeloException("modelo_no_disponible") };
        var consumo = Substitute.For<IControlConsumo>();
        consumo.Autorizar().Returns(DecisionConsumo.Permitido);

        var r = await Armar(extractor, consumo).LeerAsync(Imagen, "image/jpeg", default);

        Assert.Equal("modelo_no_disponible", r.Error);
        consumo.DidNotReceiveWithAnyArgs().Registrar(default);
    }
}
