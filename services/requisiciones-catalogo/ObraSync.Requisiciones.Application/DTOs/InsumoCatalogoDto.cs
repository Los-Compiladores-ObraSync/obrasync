using System;

namespace ObraSync.Requisiciones.Application.DTOs
{
    public class InsumoCatalogoDto
    {
        public Guid ArticuloId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string UnidadMedida { get; set; } = string.Empty;
        public bool Activo { get; set; }
    }
}
