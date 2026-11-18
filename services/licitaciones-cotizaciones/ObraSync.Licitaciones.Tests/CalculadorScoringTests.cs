using ObraSync.Licitaciones.Domain.Scoring;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class CalculadorScoringTests
{
    private static readonly Guid Articulo1 = Guid.NewGuid();
    private static readonly Guid Articulo2 = Guid.NewGuid();

    private static readonly IReadOnlyList<ItemRequerido> UnItem =
        new[] { new ItemRequerido(Articulo1, 10m) };

    private static Oferta CrearOferta(decimal precio, decimal flete, int plazo, decimal? calificacion = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), new[] { new OfertaItem(Articulo1, precio) }, flete, plazo, calificacion);

    [Fact]
    public void Si_solo_pesa_el_precio_gana_la_oferta_mas_barata()
    {
        var barata = CrearOferta(100m, 0m, 10);
        var cara = CrearOferta(120m, 0m, 2);

        var resultado = CalculadorScoring.Calcular(new[] { cara, barata }, UnItem, new PonderacionScoring(1, 0, 0));

        Assert.Equal(barata.CotizacionId, resultado[0].CotizacionId);
        Assert.Equal(1m, resultado[0].Puntaje);
        Assert.Equal(100m / 120m, resultado[1].Puntaje);
    }

    [Fact]
    public void Cambiar_la_ponderacion_cambia_la_ganadora()
    {
        var barataLenta = CrearOferta(100m, 0m, 10);
        var caraRapida = CrearOferta(120m, 0m, 2);
        var ofertas = new[] { barataLenta, caraRapida };

        var porPrecio = CalculadorScoring.Calcular(ofertas, UnItem, new PonderacionScoring(1, 0, 0));
        var porPlazo = CalculadorScoring.Calcular(ofertas, UnItem, new PonderacionScoring(0, 0, 1));

        Assert.Equal(barataLenta.CotizacionId, porPrecio[0].CotizacionId);
        Assert.Equal(caraRapida.CotizacionId, porPlazo[0].CotizacionId);
    }

    [Fact]
    public void El_precio_total_es_precio_unitario_por_cantidad_sumado_por_item()
    {
        var requeridos = new[] { new ItemRequerido(Articulo1, 10m), new ItemRequerido(Articulo2, 5m) };
        var oferta = new Oferta(
            Guid.NewGuid(), Guid.NewGuid(),
            new[] { new OfertaItem(Articulo1, 100m), new OfertaItem(Articulo2, 20m) },
            0m, 5);

        var resultado = CalculadorScoring.Calcular(new[] { oferta }, requeridos, new PonderacionScoring(1, 1, 1));

        Assert.Equal(1100m, resultado[0].PrecioTotal); // 10 x 100 + 5 x 20
    }

    [Fact]
    public void Una_oferta_que_no_cotiza_todos_los_items_se_excluye()
    {
        var requeridos = new[] { new ItemRequerido(Articulo1, 10m), new ItemRequerido(Articulo2, 5m) };
        var incompleta = CrearOferta(50m, 0m, 1); // solo cotiza el artículo 1
        var completa = new Oferta(
            Guid.NewGuid(), Guid.NewGuid(),
            new[] { new OfertaItem(Articulo1, 100m), new OfertaItem(Articulo2, 20m) },
            0m, 5);

        var resultado = CalculadorScoring.Calcular(new[] { incompleta, completa }, requeridos, new PonderacionScoring(1, 1, 1));

        Assert.Single(resultado);
        Assert.Equal(completa.CotizacionId, resultado[0].CotizacionId);
    }

    [Fact]
    public void Con_una_sola_oferta_el_puntaje_es_1()
    {
        var resultado = CalculadorScoring.Calcular(
            new[] { CrearOferta(100m, 10m, 3) }, UnItem, new PonderacionScoring(5, 3, 2));

        Assert.Equal(1m, resultado[0].Puntaje);
    }

    [Fact]
    public void Sin_ofertas_devuelve_una_lista_vacia()
    {
        var resultado = CalculadorScoring.Calcular(Array.Empty<Oferta>(), UnItem, new PonderacionScoring(1, 1, 1));

        Assert.Empty(resultado);
    }

    [Fact]
    public void La_calificacion_pesa_cero_por_defecto_y_no_influye()
    {
        var a = CrearOferta(100m, 5m, 3, calificacion: 5m);
        var b = CrearOferta(100m, 5m, 3, calificacion: 1m);

        var resultado = CalculadorScoring.Calcular(new[] { a, b }, UnItem, new PonderacionScoring(1, 1, 1));

        Assert.Equal(resultado[0].Puntaje, resultado[1].Puntaje);
    }

    [Fact]
    public void Si_la_calificacion_tiene_peso_gana_la_de_mayor_calificacion()
    {
        var a = CrearOferta(100m, 5m, 3, calificacion: 5m);
        var b = CrearOferta(100m, 5m, 3, calificacion: 1m);

        var resultado = CalculadorScoring.Calcular(new[] { b, a }, UnItem, new PonderacionScoring(0, 0, 0, 1));

        Assert.Equal(a.CotizacionId, resultado[0].CotizacionId);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 0.5)]
    [InlineData(5, 1)]
    public void Normalizacion_de_calificacion_usa_escala_fija_1_a_5(decimal calificacion, decimal puntajeEsperado)
    {
        var oferta = CrearOferta(100m, 10m, 5, calificacion: calificacion);

        var resultado = CalculadorScoring.Calcular(
            new[] { oferta },
            UnItem,
            new PonderacionScoring(0m, 0m, 0m, 1m));

        Assert.Single(resultado);
        Assert.Equal(puntajeEsperado, resultado[0].PuntajesCriterio!.Calificacion);
        Assert.Equal(puntajeEsperado, resultado[0].Puntaje);
    }

    [Fact]
    public void Proveedor_sin_calificacion_obtiene_cero_y_peso_calificacion_se_mantiene_en_divisor()
    {
        var conCalificacion5 = CrearOferta(100m, 10m, 5, calificacion: 5m);
        var sinCalificacion = CrearOferta(100m, 10m, 5, calificacion: null);
        var conCalificacion1 = CrearOferta(100m, 10m, 5, calificacion: 1m);

        var ponderacion = new PonderacionScoring(0.50m, 0.20m, 0.20m, 0.10m);
        var resultado = CalculadorScoring.Calcular(
            new[] { sinCalificacion, conCalificacion5, conCalificacion1 },
            UnItem,
            ponderacion);

        var eval5 = resultado.Single(r => r.CotizacionId == conCalificacion5.CotizacionId);
        var evalNull = resultado.Single(r => r.CotizacionId == sinCalificacion.CotizacionId);
        var eval1 = resultado.Single(r => r.CotizacionId == conCalificacion1.CotizacionId);

        Assert.Equal(1m, eval5.PuntajesCriterio!.Calificacion);
        Assert.Equal(1m, eval5.Puntaje);

        Assert.Equal(0m, evalNull.PuntajesCriterio!.Calificacion);
        Assert.Equal(0.90m, evalNull.Puntaje);
        Assert.Equal(eval1.Puntaje, evalNull.Puntaje);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    public void Calificacion_fuera_de_rango_1_a_5_lanza_ArgumentOutOfRangeException(decimal calificacionInvalida)
    {
        var oferta = CrearOferta(100m, 10m, 5, calificacion: calificacionInvalida);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CalculadorScoring.Calcular(new[] { oferta }, UnItem, new PonderacionScoring(0.50m, 0.20m, 0.20m, 0.10m)));
    }

    [Fact]
    public void Pesos_que_no_suman_uno_se_normalizan_proporcionalmente()
    {
        var masBarata = CrearOferta(100m, 100m, 10, calificacion: 1m);
        var mejorCalificada = CrearOferta(200m, 100m, 10, calificacion: 5m);

        var rUnitario = CalculadorScoring.Calcular(
            new[] { masBarata, mejorCalificada },
            UnItem,
            new PonderacionScoring(0.20m, 0.20m, 0.20m, 0.40m));

        var rEscala100 = CalculadorScoring.Calcular(
            new[] { masBarata, mejorCalificada },
            UnItem,
            new PonderacionScoring(20m, 20m, 20m, 40m));

        Assert.Equal(rUnitario[0].CotizacionId, rEscala100[0].CotizacionId);
        Assert.Equal(rUnitario[0].Puntaje, rEscala100[0].Puntaje);
        Assert.Equal(rUnitario[1].Puntaje, rEscala100[1].Puntaje);
    }

    [Fact]
    public void Los_pesos_negativos_o_todos_cero_son_invalidos()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PonderacionScoring(-1, 1, 1));
        Assert.Throws<ArgumentException>(() => new PonderacionScoring(0, 0, 0));
    }

    [Fact]
    public void El_aporte_de_cada_criterio_suma_exactamente_el_puntaje()
    {
        var a = CrearOferta(100m, 500m, 10, 3m);
        var b = CrearOferta(130m, 200m, 4, 5m);
        var c = CrearOferta(115m, 350m, 7, 1m);

        var resultado = CalculadorScoring.Calcular(new[] { a, b, c }, UnItem, new PonderacionScoring(0.5m, 0.2m, 0.2m, 0.1m));

        Assert.All(resultado, r =>
        {
            Assert.NotNull(r.Aporte);
            var suma = r.Aporte!.Precio + r.Aporte.Flete + r.Aporte.Plazo + r.Aporte.Calificacion;
            Assert.Equal(r.Puntaje, suma);
        });
    }

    [Fact]
    public void Un_criterio_sin_peso_no_aporta_nada()
    {
        var barata = CrearOferta(100m, 0m, 10);
        var cara = CrearOferta(120m, 0m, 2);

        var resultado = CalculadorScoring.Calcular(new[] { barata, cara }, UnItem, new PonderacionScoring(1, 0, 0));

        var ganadora = resultado[0];
        Assert.Equal(1m, ganadora.Aporte!.Precio);
        Assert.Equal(0m, ganadora.Aporte.Flete);
        Assert.Equal(0m, ganadora.Aporte.Plazo);
        Assert.Equal(0m, ganadora.Aporte.Calificacion);
    }
}
