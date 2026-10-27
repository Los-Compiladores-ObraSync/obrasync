namespace ObraSync.Licitaciones.Domain.Scoring;

public static class CalculadorScoring
{
    /// <summary>
    /// Devuelve las ofertas completas ordenadas de mayor a menor puntaje (0 a 1).
    /// Una oferta que no cotiza todos los ítems del pliego se excluye.
    /// </summary>
    public static IReadOnlyList<ResultadoScoring> Calcular(
        IReadOnlyList<Oferta> ofertas,
        IReadOnlyList<ItemRequerido> requeridos,
        PonderacionScoring ponderacion)
    {
        ArgumentNullException.ThrowIfNull(ofertas);
        ArgumentNullException.ThrowIfNull(requeridos);
        ArgumentNullException.ThrowIfNull(ponderacion);
        if (requeridos.Count == 0)
            throw new ArgumentException("El pliego debe tener al menos un ítem.", nameof(requeridos));

        var completas = new List<(Oferta Oferta, decimal Precio)>();
        foreach (var oferta in ofertas)
        {
            var precio = PrecioTotal(oferta, requeridos);
            if (precio is not null) completas.Add((oferta, precio.Value));
        }

        if (completas.Count == 0) return Array.Empty<ResultadoScoring>();

        var precios = completas.Select(c => c.Precio).ToList();
        var fletes = completas.Select(c => c.Oferta.Flete).ToList();
        var plazos = completas.Select(c => (decimal)c.Oferta.PlazoEntregaDias).ToList();
        var calificaciones = completas.Select(c => c.Oferta.Calificacion ?? 0m).ToList();

        return completas
            .Select(c =>
            {
                var suma =
                    ponderacion.Precio * MenorEsMejor(c.Precio, precios) +
                    ponderacion.Flete * MenorEsMejor(c.Oferta.Flete, fletes) +
                    ponderacion.Plazo * MenorEsMejor(c.Oferta.PlazoEntregaDias, plazos) +
                    ponderacion.Calificacion * MayorEsMejor(c.Oferta.Calificacion ?? 0m, calificaciones);

                return new ResultadoScoring(
                    c.Oferta.CotizacionId, c.Oferta.ProveedorId, c.Precio, suma / ponderacion.Total);
            })
            .OrderByDescending(r => r.Puntaje)
            .ToList();
    }

    // Suma precioUnitario x cantidad de cada ítem del pliego. Null si falta cotizar alguno.
    private static decimal? PrecioTotal(Oferta oferta, IReadOnlyList<ItemRequerido> requeridos)
    {
        decimal total = 0;
        foreach (var requerido in requeridos)
        {
            var item = oferta.Items.FirstOrDefault(i => i.ArticuloId == requerido.ArticuloId);
            if (item is null) return null;
            total += item.PrecioUnitario * requerido.Cantidad;
        }
        return total;
    }

    // Normaliza a 0..1: la mejor oferta saca 1 y la peor 0. Si todas son iguales, todas sacan 1.
    private static decimal MenorEsMejor(decimal valor, IReadOnlyList<decimal> valores)
    {
        var min = valores.Min();
        var max = valores.Max();
        return max == min ? 1m : (max - valor) / (max - min);
    }

    private static decimal MayorEsMejor(decimal valor, IReadOnlyList<decimal> valores)
    {
        var min = valores.Min();
        var max = valores.Max();
        return max == min ? 1m : (valor - min) / (max - min);
    }
}