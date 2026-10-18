using System;
using ObraSync.Requisiciones.Domain.Constants;

namespace ObraSync.Requisiciones.Domain.Entities
{
    public class Insumo
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string UnidadMedida { get; set; } = string.Empty;
        public bool Activo { get; set; } = true;

        public Insumo() { }

        public Insumo(Guid id, string codigo, string descripcion, string unidadMedida, bool activo = true)
        {
            if (string.IsNullOrWhiteSpace(codigo))
                throw new ArgumentException("El código del insumo no puede estar vacío.", nameof(codigo));

            if (string.IsNullOrWhiteSpace(descripcion))
                throw new ArgumentException("La descripción del insumo no puede estar vacía.", nameof(descripcion));

            if (!UnidadesMedida.EsValida(unidadMedida))
                throw new ArgumentException($"La unidad de medida '{unidadMedida}' no es válida para el catálogo de obra.", nameof(unidadMedida));

            Id = id == Guid.Empty ? Guid.NewGuid() : id;
            Codigo = codigo.Trim().ToUpperInvariant();
            Descripcion = descripcion.Trim();
            UnidadMedida = unidadMedida.Trim().ToUpperInvariant();
            Activo = activo;
        }

        public void Desactivar() => Activo = false;
        public void Activar() => Activo = true;
    }
}
