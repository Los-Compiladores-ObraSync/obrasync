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
        Assert.Equal(0m, resultado[1].Puntaje);
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
        var a = CrearOferta(100m, 5m, 3, calificacion: 10m);
        var b = CrearOferta(100m, 5m, 3, calificacion: 1m);

        var resultado = CalculadorScoring.Calcular(new[] { a, b }, UnItem, new PonderacionScoring(1, 1, 1));

        Assert.Equal(resultado[0].Puntaje, resultado[1].Puntaje);
    }

    [Fact]
    public void Si_la_calificacion_tiene_peso_gana_la_de_mayor_calificacion()
    {
        var a = CrearOferta(100m, 5m, 3, calificacion: 10m);
        var b = CrearOferta(100m, 5m, 3, calificacion: 1m);

        var resultado = CalculadorScoring.Calcular(new[] { b, a }, UnItem, new PonderacionScoring(0, 0, 0, 1));

        Assert.Equal(a.CotizacionId, resultado[0].CotizacionId);
    }

    [Fact]
    public void Los_pesos_negativos_o_todos_cero_son_invalidos()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PonderacionScoring(-1, 1, 1));
        Assert.Throws<ArgumentException>(() => new PonderacionScoring(0, 0, 0));
    }
}