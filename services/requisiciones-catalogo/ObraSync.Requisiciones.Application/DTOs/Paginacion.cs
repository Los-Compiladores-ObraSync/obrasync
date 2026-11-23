using System.Collections.Generic;
using ObraSync.Requisiciones.Application.Exceptions;

namespace ObraSync.Requisiciones.Application.DTOs
{
    public sealed record ParametrosPagina(int Pagina = 1, int Tamano = 50)
    {
        public const int TamanoMaximo = 50;

        public static ParametrosPagina Crear(int pagina = 1, int tamano = TamanoMaximo)
        {
            var parametros = new ParametrosPagina(pagina, tamano);
            var error = parametros.Validar();
            if (error is not null)
            {
                throw new PaginacionInvalidaException(error);
            }

            return parametros;
        }

        public string? Validar()
        {
            if (Pagina < 1)
                return "La página debe ser 1 o mayor.";
            if (Tamano < 1 || Tamano > TamanoMaximo)
                return $"El tamaño debe estar entre 1 y {TamanoMaximo}.";
            return null;
        }

        public int Saltear => (Pagina - 1) * Tamano;
    }

    public sealed record PaginaDe<T>(IReadOnlyList<T> Items, int Total);
}
