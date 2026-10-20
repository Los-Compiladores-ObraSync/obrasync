using System;
using ObraSync.Requisiciones.Domain.Constants;

namespace ObraSync.Requisiciones.Domain.Entities
{
    public class RequisicionItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid RequisicionId { get; set; }
        public Guid ArticuloId { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
        public string UnidadMedida { get; set; } = string.Empty;

        public RequisicionItem() { }

        public RequisicionItem(
            Guid id,
            Guid requisicionId,
            Guid articuloId,
            string descripcion,
            decimal cantidad,
            string unidadMedida)
        {
            if (articuloId == Guid.Empty)
                throw new ArgumentException("El identificador del artículo (ArticuloId) es obligatorio.", nameof(articuloId));

            if (string.IsNullOrWhiteSpace(descripcion))
                throw new ArgumentException("La descripción del artículo no puede estar vacía.", nameof(descripcion));

            if (cantidad <= 0)
                throw new ArgumentException("La cantidad requerida debe ser mayor a cero.", nameof(cantidad));

            if (!UnidadesMedida.EsValida(unidadMedida))
                throw new ArgumentException($"La unidad de medida '{unidadMedida}' no es válida.", nameof(unidadMedida));

            Id = id == Guid.Empty ? Guid.NewGuid() : id;
            RequisicionId = requisicionId;
            ArticuloId = articuloId;
            Descripcion = descripcion.Trim();
            Cantidad = cantidad;
            UnidadMedida = unidadMedida.Trim().ToUpperInvariant();
        }
    }
}
