namespace ObraSync.Requisiciones.Domain.Constants
{
    public static class EstadoRequisicion
    {
        public const string Pendiente = "Pendiente";
        public const string Aprobada = "Aprobada";
        public const string Rechazada = "Rechazada";
        /// <summary>El Capataz (o un Admin) la anuló antes de que se aprobara. No genera licitación.</summary>
        public const string Cancelada = "Cancelada";
    }
}
