using System.Collections.Generic;

namespace ObraSync.Requisiciones.Domain.Constants
{
    public static class UnidadesMedida
    {
        public const string Bolsa = "BOLSA";
        public const string Kilogramo = "KG";
        public const string MetroCubico = "M3";
        public const string Unidad = "UNIDAD";

        private static readonly HashSet<string> Validas = new()
        {
            Bolsa,
            Kilogramo,
            MetroCubico,
            Unidad
        };

        public static bool EsValida(string? unidad) =>
            !string.IsNullOrWhiteSpace(unidad) && Validas.Contains(unidad.Trim().ToUpperInvariant());
    }
}
