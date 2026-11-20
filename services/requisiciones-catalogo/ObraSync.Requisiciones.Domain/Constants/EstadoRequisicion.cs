using System.Collections.Generic;

namespace ObraSync.Requisiciones.Domain.Constants
{
    public static class EstadoRequisicion
    {
        public const string Pendiente = "Pendiente";
        public const string Aprobada = "Aprobada";
        public const string Rechazada = "Rechazada";
        /// <summary>El Capataz (o un Admin) la anuló antes de que se aprobara. No genera licitación.</summary>
        public const string Cancelada = "Cancelada";

        private static readonly HashSet<string> Validos = new(System.StringComparer.OrdinalIgnoreCase)
        {
            Pendiente,
            Aprobada,
            Rechazada,
            Cancelada
        };

        public static bool EsValido(string? estado) =>
            !string.IsNullOrWhiteSpace(estado) && Validos.Contains(estado.Trim());
    }
}
