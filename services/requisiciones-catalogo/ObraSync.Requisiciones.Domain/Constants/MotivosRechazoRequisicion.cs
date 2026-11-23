using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ObraSync.Requisiciones.Domain.Constants
{
    /// <summary>
    /// Motivos estandarizados y reglas de validación para rechazar una requisición (#250).
    /// Soporta tanto el contrato estructurado (categoriaMotivo + comentario opcional/obligatorio para "Otro")
    /// como el campo legacy "motivo", bloqueando siempre texto basura o sin sentido.
    /// </summary>
    public static class MotivosRechazoRequisicion
    {
        public const string FaltaPresupuesto = "Falta de presupuesto";
        public const string PrecioFueraDeRango = "Precio fuera de rango";
        public const string CantidadIncorrecta = "Cantidad incorrecta";
        public const string MaterialIncorrecto = "Material o especificación incorrecta";
        public const string PedidoDuplicado = "Pedido duplicado";
        public const string Otro = "Otro";

        public const int LongitudMinimaDetalleOtro = 10;
        public const int LongitudMinimaTextoLibre = 8;
        public const int LongitudMaximaMotivo = 500;

        private static readonly HashSet<char> Vocales = new()
        {
            'a', 'e', 'i', 'o', 'u', 'y'
        };

        private static readonly string[] SecuenciasTeclado =
        {
            "qwer", "wert", "erty", "rtyu", "tyui", "yuio", "uiop",
            "asdf", "sdfg", "dfgh", "fghj", "ghjk", "hjkl",
            "zxcv", "xcvb", "cvbn", "vbnm",
            "qazw", "wsxe", "edcr"
        };

        public static readonly IReadOnlyList<string> Permitidos = new[]
        {
            FaltaPresupuesto,
            PrecioFueraDeRango,
            CantidadIncorrecta,
            MaterialIncorrecto,
            PedidoDuplicado,
            Otro
        };

        /// <summary>
        /// Devuelve la categoría canónica si coincide (ignorando mayúsculas, tildes y espacios extremos);
        /// devuelve null si no pertenece a la lista permitida.
        /// </summary>
        public static string? NormalizarCategoria(string? categoria)
        {
            if (string.IsNullOrWhiteSpace(categoria))
            {
                return null;
            }

            var normalizada = NormalizarSinTildes(categoria.Trim());
            return Permitidos.FirstOrDefault(m =>
                string.Equals(NormalizarSinTildes(m), normalizada, StringComparison.OrdinalIgnoreCase));
        }

        public static bool EsCategoriaValida(string? categoria) => NormalizarCategoria(categoria) is not null;

        /// <summary>
        /// Valida y resuelve el texto final del motivo de rechazo.
        /// Lanza ArgumentException (traducida a 400 ProblemDetails) si el motivo o comentario es inválido.
        /// </summary>
        public static string ResolverYValidar(
            string? motivo,
            string? categoriaMotivo = null,
            string? comentario = null)
        {
            // Modo estructurado (#250 / #239): se especificó categoriaMotivo explícitamente.
            if (!string.IsNullOrWhiteSpace(categoriaMotivo))
            {
                var categoriaCanonica = NormalizarCategoria(categoriaMotivo);
                if (categoriaCanonica is null)
                {
                    throw new ArgumentException(
                        $"El motivo de rechazo '{categoriaMotivo.Trim()}' no es válido. Opciones permitidas: {string.Join(", ", Permitidos)}.",
                        nameof(categoriaMotivo));
                }

                var comentarioEfectivo = !string.IsNullOrWhiteSpace(comentario)
                    ? comentario.Trim()
                    : (!string.IsNullOrWhiteSpace(motivo) && NormalizarCategoria(motivo) is null
                        ? motivo.Trim()
                        : null);

                return ComponerYValidarPorCategoria(categoriaCanonica, comentarioEfectivo);
            }

            // Modo retrocompatible: el cliente envió solo "motivo" (y opcionalmente "comentario").
            if (string.IsNullOrWhiteSpace(motivo))
            {
                throw new ArgumentException("El motivo de rechazo es obligatorio.", nameof(motivo));
            }

            var motivoLimpio = motivo.Trim();
            if (motivoLimpio.Length > LongitudMaximaMotivo)
            {
                throw new ArgumentException(
                    $"El motivo de rechazo no puede superar los {LongitudMaximaMotivo} caracteres.",
                    nameof(motivo));
            }

            // Si "motivo" coincide directamente con una categoría oficial:
            var categoriaDesdeMotivo = NormalizarCategoria(motivoLimpio);
            if (categoriaDesdeMotivo is not null)
            {
                var comentarioOpcional = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();
                return ComponerYValidarPorCategoria(categoriaDesdeMotivo, comentarioOpcional);
            }

            // Si "motivo" viene prefijado como "<Categoría>: <detalle>" (por ejemplo "Otro: ..."):
            var indiceSeparador = motivoLimpio.IndexOf(':');
            if (indiceSeparador > 0)
            {
                var posibleCategoria = NormalizarCategoria(motivoLimpio[..indiceSeparador]);
                if (posibleCategoria is not null)
                {
                    var detalleDespuesDeDosPuntos = motivoLimpio[(indiceSeparador + 1)..].Trim();
                    var comentarioEfectivo = !string.IsNullOrWhiteSpace(detalleDespuesDeDosPuntos)
                        ? detalleDespuesDeDosPuntos
                        : (string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim());

                    return ComponerYValidarPorCategoria(posibleCategoria, comentarioEfectivo);
                }
            }

            // Texto libre (clientes anteriores a #239 o asistente): validar que sea descriptivo y no texto basura.
            ValidarTextoLibre(motivoLimpio, LongitudMinimaTextoLibre, exigirMinimoParaPalabraUnica: LongitudMinimaDetalleOtro);

            if (!string.IsNullOrWhiteSpace(comentario))
            {
                var comentarioLimpio = comentario.Trim();
                ValidarTextoLibre(comentarioLimpio, 3, exigirMinimoParaPalabraUnica: 4);
                var combinado = $"{motivoLimpio}: {comentarioLimpio}";
                if (combinado.Length > LongitudMaximaMotivo)
                {
                    throw new ArgumentException(
                        $"El motivo de rechazo no puede superar los {LongitudMaximaMotivo} caracteres.",
                        nameof(comentario));
                }

                return combinado;
            }

            return motivoLimpio;
        }

        private static string ComponerYValidarPorCategoria(string categoriaCanonica, string? comentario)
        {
            if (string.Equals(categoriaCanonica, Otro, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(comentario) || comentario.Length < LongitudMinimaDetalleOtro)
                {
                    throw new ArgumentException(
                        $"Cuando el motivo de rechazo es '{Otro}', debés ingresar un comentario descriptivo de al menos {LongitudMinimaDetalleOtro} caracteres.",
                        nameof(comentario));
                }

                ValidarTextoLibre(comentario, LongitudMinimaDetalleOtro, exigirMinimoParaPalabraUnica: LongitudMinimaDetalleOtro);

                var compuestoOtro = $"{Otro}: {comentario}";
                if (compuestoOtro.Length > LongitudMaximaMotivo)
                {
                    throw new ArgumentException(
                        $"El motivo de rechazo no puede superar los {LongitudMaximaMotivo} caracteres.",
                        nameof(comentario));
                }

                return compuestoOtro;
            }

            if (string.IsNullOrWhiteSpace(comentario))
            {
                return categoriaCanonica;
            }

            ValidarTextoLibre(comentario, 3, exigirMinimoParaPalabraUnica: 4);

            var compuesto = $"{categoriaCanonica}: {comentario}";
            if (compuesto.Length > LongitudMaximaMotivo)
            {
                throw new ArgumentException(
                    $"El motivo de rechazo no puede superar los {LongitudMaximaMotivo} caracteres.",
                    nameof(comentario));
            }

            return compuesto;
        }

        private static void ValidarTextoLibre(string texto, int longitudMinima, int exigirMinimoParaPalabraUnica)
        {
            if (texto.Length < longitudMinima)
            {
                throw new ArgumentException(
                    $"El motivo de rechazo debe ser descriptivo (al menos {longitudMinima} caracteres) o elegí una opción válida de la lista.",
                    nameof(texto));
            }

            var palabras = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (palabras.Length == 1 && texto.Length < exigirMinimoParaPalabraUnica)
            {
                throw new ArgumentException(
                    $"El motivo de rechazo debe ser descriptivo (al menos {exigirMinimoParaPalabraUnica} caracteres) o elegí una opción válida de la lista.",
                    nameof(texto));
            }

            if (EsTextoBasura(texto))
            {
                throw new ArgumentException(
                    "El motivo de rechazo no parece un texto válido. Ingresá una explicación clara o elegí un motivo de la lista.",
                    nameof(texto));
            }
        }

        /// <summary>
        /// Detecta entradas basura típicas (tecleo aleatorio, repeticiones, solo números/símbolos, "asdasdas", "qwerty").
        /// </summary>
        public static bool EsTextoBasura(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return true;
            }

            var limpio = texto.Trim();
            var sinTildes = NormalizarSinTildes(limpio).ToLowerInvariant();
            var soloLetras = new string(sinTildes.Where(char.IsLetter).ToArray());

            // 1. Muy pocas letras (ej. solo números o signos: "1234567890", "........")
            if (soloLetras.Length < 3)
            {
                return true;
            }

            // 2. Cuatro o más caracteres iguales seguidos (ej. "aaaaaa", "zzzzzz", "......")
            var repeticionesSeguidas = 1;
            for (var i = 1; i < sinTildes.Length; i++)
            {
                if (sinTildes[i] == sinTildes[i - 1] && !char.IsWhiteSpace(sinTildes[i]))
                {
                    repeticionesSeguidas++;
                    if (repeticionesSeguidas >= 4)
                    {
                        return true;
                    }
                }
                else
                {
                    repeticionesSeguidas = 1;
                }
            }

            // 3. Texto de 6+ letras con 2 o menos letras distintas (ej. "abababab", "jajaja")
            if (soloLetras.Length >= 6 && soloLetras.Distinct().Count() <= 2)
            {
                return true;
            }

            // 4. Patrón cíclico corto repetido que cubre casi todo el texto (ej. "asdasdas", "asdfasdf", "qweqweqwe", "lalalala")
            if (soloLetras.Length >= 6 && TienePatronCiclicoRepetido(soloLetras))
            {
                return true;
            }

            var palabras = sinTildes
                .Split(new[] { ' ', '\t', '\r', '\n', ',', '.', ';', ':', '-', '_', '/', '(', ')', '!', '¡', '?', '¿' },
                    StringSplitOptions.RemoveEmptyEntries);

            // 5. Análisis por palabra: secuencias de teclado imposibles en español, palabras sin vocales,
            //    5+ consonantes consecutivas o repetición cíclica (ej. "asdasdas", "asdfasdf", "kjsdhfkjsdhf")
            foreach (var palabra in palabras)
            {
                var letrasPalabra = new string(palabra.Where(char.IsLetter).ToArray());
                if (letrasPalabra.Length >= 4 && SecuenciasTeclado.Any(seq => letrasPalabra.Contains(seq, StringComparison.Ordinal)))
                {
                    return true;
                }

                if (letrasPalabra.Length >= 5 && !letrasPalabra.Any(c => Vocales.Contains(c)))
                {
                    return true;
                }

                if (TieneConsonantesConsecutivasExcesivas(letrasPalabra, umbral: 5))
                {
                    return true;
                }

                if (letrasPalabra.Length >= 6 && TienePatronCiclicoRepetido(letrasPalabra))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TienePatronCiclicoRepetido(string letras)
        {
            for (var periodo = 2; periodo <= 4; periodo++)
            {
                if (letras.Length < periodo * 2)
                {
                    continue;
                }

                var coincidencias = 0;
                for (var i = periodo; i < letras.Length; i++)
                {
                    if (letras[i] == letras[i % periodo])
                    {
                        coincidencias++;
                    }
                }

                var totalComparado = letras.Length - periodo;
                if (totalComparado > 0 && (double)coincidencias / totalComparado >= 0.85)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TieneConsonantesConsecutivasExcesivas(string letras, int umbral)
        {
            var consecutivas = 0;
            foreach (var c in letras)
            {
                if (!Vocales.Contains(c))
                {
                    consecutivas++;
                    if (consecutivas >= umbral)
                    {
                        return true;
                    }
                }
                else
                {
                    consecutivas = 0;
                }
            }

            return false;
        }

        private static string NormalizarSinTildes(string texto)
        {
            var normalizado = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalizado.Length);
            foreach (var c in normalizado)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
