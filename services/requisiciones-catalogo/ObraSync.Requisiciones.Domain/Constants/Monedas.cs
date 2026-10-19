namespace ObraSync.Requisiciones.Domain.Constants
{
    public static class Monedas
    {
        public const string PesoArgentino = "ARS";

        public static bool EsValida(string? moneda) =>
            !string.IsNullOrWhiteSpace(moneda) &&
            string.Equals(moneda.Trim(), PesoArgentino, System.StringComparison.OrdinalIgnoreCase);
    }
}
