namespace ObraSync.Licitaciones.Domain.Scoring;

public static class CalculadorScoring
{
    /// <summary>
    /// Devuelve las ofertas completas ordenadas de mayor a menor puntaje (0 a 1).
    /// Una oferta que no cotiza todos los ítems del pliego se excluye.
    /// Por defecto normaliza cada criterio respecto a la mejor oferta (mejor / valor, #197) para no castigar
    /// con 0 a una oferta apenas más cara.
    /// </summary>
    public static IReadOnlyList<ResultadoScoring> Calcular(
        IReadOnlyList<Oferta> ofertas,
        IReadOnlyList<ItemRequerido> requeridos,
        PonderacionScoring ponderacion,
        ModoNormalizacionScoring modoNormalizacion = ModoNormalizacionScoring.RelativoMejorOferta)
    {
        ArgumentNullException.ThrowIfNull(ofertas);
        ArgumentNullException.ThrowIfNull(requeridos);
        ArgumentNullException.ThrowIfNull(ponderacion);
        if (requeridos.Count == 0)
            throw new ArgumentException("El pliego debe tener al menos un ítem.", nameof(requeridos));

        var completas = new List<(Oferta Oferta, decimal Precio)>();
        foreach (var oferta in ofertas)
        {
            if (oferta.Calificacion.HasValue && (oferta.Calificacion.Value < 1m || oferta.Calificacion.Value > 5m))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ofertas),
                    oferta.Calificacion.Value,
                    "La calificación del proveedor debe estar entre 1 y 5.");
            }

            var precio = PrecioTotal(oferta, requeridos);
            if (precio is not null) completas.Add((oferta, precio.Value));
        }

        if (completas.Count == 0) return Array.Empty<ResultadoScoring>();

        var precios = completas.Select(c => c.Precio).ToList();
        var fletes = completas.Select(c => c.Oferta.Flete).ToList();
        var plazos = completas.Select(c => (decimal)c.Oferta.PlazoEntregaDias).ToList();

        // Regla #88: el divisor es siempre la suma total de los 4 pesos (incluyendo calificación).
        // Si un proveedor no tiene calificación registrada, su puntaje normalizado es 0 y el peso
        // de calificación se mantiene en el divisor (penaliza igual que calificación 1).
        var pesoTotalEfectivo = ponderacion.Total;

        return completas
            .Select(c =>
            {
                decimal normPrecio = modoNormalizacion == ModoNormalizacionScoring.MinMax
                    ? MenorEsMejorMinMax(c.Precio, precios)
                    : MenorEsMejorRelativo(c.Precio, precios);

                decimal normFlete = modoNormalizacion == ModoNormalizacionScoring.MinMax
                    ? MenorEsMejorMinMax(c.Oferta.Flete, fletes)
                    : MenorEsMejorRelativo(c.Oferta.Flete, fletes);

                decimal normPlazo = modoNormalizacion == ModoNormalizacionScoring.MinMax
                    ? MenorEsMejorMinMax(c.Oferta.PlazoEntregaDias, plazos)
                    : MenorEsMejorRelativo(c.Oferta.PlazoEntregaDias, plazos);

                decimal normCalificacion = NormalizarCalificacion(c.Oferta.Calificacion);

                var puntajesCriterio = new PuntajesCriterioScoring(
                    normPrecio,
                    normFlete,
                    normPlazo,
                    normCalificacion);

                var aporte = new AporteScoring(
                    ponderacion.Precio * normPrecio / pesoTotalEfectivo,
                    ponderacion.Flete * normFlete / pesoTotalEfectivo,
                    ponderacion.Plazo * normPlazo / pesoTotalEfectivo,
                    ponderacion.Calificacion * normCalificacion / pesoTotalEfectivo);

                // El puntaje es la suma de los aportes: el desglose y el total no pueden discrepar.
                return new ResultadoScoring(
                    c.Oferta.CotizacionId,
                    c.Oferta.ProveedorId,
                    c.Precio,
                    aporte.Precio + aporte.Flete + aporte.Plazo + aporte.Calificacion,
                    aporte,
                    puntajesCriterio);
            })
            .OrderByDescending(r => r.Puntaje)
            .ToList();
    }

    // Suma precioUnitario x cantidad (o cantidadOfrecida si cotizó presentación alternativa, #190) de cada ítem del pliego. Null si falta cotizar alguno.
    private static decimal? PrecioTotal(Oferta oferta, IReadOnlyList<ItemRequerido> requeridos)
    {
        decimal total = 0;
        foreach (var requerido in requeridos)
        {
            var item = oferta.Items.FirstOrDefault(i => i.ArticuloId == requerido.ArticuloId);
            if (item is null) return null;
            var cantidadEfectiva = item.CantidadOfrecida is > 0 ? item.CantidadOfrecida.Value : requerido.Cantidad;
            total += item.PrecioUnitario * cantidadEfectiva;
        }
        return total;
    }

    // Normalización relativa a la mejor oferta (mejor / valor, #197):
    // la mejor saca 1 y una oferta 1% más cara saca 100/101 (~0.9901) en lugar de 0.
    private static decimal MenorEsMejorRelativo(decimal valor, IReadOnlyList<decimal> valores)
    {
        var min = valores.Min();
        var max = valores.Max();
        if (max == min) return 1m;
        if (valor <= 0m) return 1m;
        if (min > 0m) return min / valor;
        return (max - valor) / max;
    }

    // Normalización lineal de calificación en escala 1..5 (#88):
    // 1 vale 0, 5 vale 1 ((calificacion - 1) / 4). Si no tiene calificación (null), vale 0.
    private static decimal NormalizarCalificacion(decimal? calificacion)
    {
        if (!calificacion.HasValue)
        {
            return 0m;
        }

        return (calificacion.Value - 1m) / 4m;
    }

    // Normalización lineal min-max (0..1)
    private static decimal MenorEsMejorMinMax(decimal valor, IReadOnlyList<decimal> valores)
    {
        var min = valores.Min();
        var max = valores.Max();
        return max == min ? 1m : (max - valor) / (max - min);
    }
}
