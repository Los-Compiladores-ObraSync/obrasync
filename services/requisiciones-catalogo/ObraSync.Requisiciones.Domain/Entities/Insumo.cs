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
        public string Moneda { get; set; } = Monedas.PesoArgentino;
        public decimal? PrecioReferencia { get; set; }
        public bool Activo { get; set; } = true;

        public Insumo() { }

        public Insumo(
            Guid id,
            string codigo,
            string descripcion,
            string unidadMedida,
            bool activo = true,
            decimal? precioReferencia = null,
            string moneda = Monedas.PesoArgentino)
        {
            if (string.IsNullOrWhiteSpace(codigo))
                throw new ArgumentException("El código del insumo no puede estar vacío.", nameof(codigo));

            if (string.IsNullOrWhiteSpace(descripcion))
                throw new ArgumentException("La descripción del insumo no puede estar vacía.", nameof(descripcion));

            if (!UnidadesMedida.EsValida(unidadMedida))
                throw new ArgumentException($"La unidad de medida '{unidadMedida}' no es válida para el catálogo de obra.", nameof(unidadMedida));

            if (!Monedas.EsValida(moneda))
                throw new ArgumentException($"La moneda '{moneda}' no es válida. El catálogo opera en una sola divisa ({Monedas.PesoArgentino}).", nameof(moneda));

            if (precioReferencia.HasValue && precioReferencia.Value < 0)
                throw new ArgumentException("El precio de referencia no puede ser negativo.", nameof(precioReferencia));

            Id = id == Guid.Empty ? Guid.NewGuid() : id;
            Codigo = codigo.Trim().ToUpperInvariant();
            Descripcion = descripcion.Trim();
            UnidadMedida = unidadMedida.Trim().ToUpperInvariant();
            Moneda = Monedas.PesoArgentino;
            PrecioReferencia = precioReferencia;
            Activo = activo;
        }

        public void Desactivar() => Activo = false;
        public void Activar() => Activo = true;
    }
}
