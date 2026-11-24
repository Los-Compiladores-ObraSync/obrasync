// Cómo se muestra el plazo de entrega de una cotización.
// El contrato (contracts/openapi) lo define en DÍAS CORRIDOS, entero positivo (campo plazoEntregaDias).
// Un solo formateador para todas las pantallas: así ninguna muestra un plazo sin su unidad y todas
// escriben "1 día" / "5 días" igual.

/** "1 día" o "5 días". */
export function formatearPlazoEntrega(dias: number): string {
  return `${dias} ${dias === 1 ? 'día' : 'días'}`
}

/** Igual que formatearPlazoEntrega, pero "Inmediato" si el plazo no viene informado o es 0. */
export function formatearPlazoOInmediato(dias: number | null | undefined): string {
  return dias ? formatearPlazoEntrega(dias) : 'Inmediato'
}
