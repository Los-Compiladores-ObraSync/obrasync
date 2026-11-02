// Fechas que llegan de la API (ISO 8601, ej.: "2026-10-19T12:00:00Z") escritas para mostrar en pantalla.

/** "2026-10-19T12:00:00Z" → "19 de octubre de 2026" (vacío si no es una fecha válida). */
export function fechaLarga(iso: string | null | undefined): string {
  if (!iso) return ''
  const fecha = new Date(iso)
  if (Number.isNaN(fecha.getTime())) return ''
  return new Intl.DateTimeFormat('es-AR', {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  }).format(fecha)
}
