// Escribe una fecha del campo de fecha (AAAA-MM-DD) con palabras, para confirmarla de un vistazo.

/** "AAAA-MM-DD" → Date a las 00:00 locales (undefined si el texto no es una fecha válida). */
function deTextoFecha(texto: string): Date | undefined {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(texto)
  if (!m) return undefined
  const anio = Number(m[1])
  const mes = Number(m[2]) - 1
  const dia = Number(m[3])
  const fecha = new Date(anio, mes, dia)
  // Si el día no existe (ej.: 31 de febrero) JavaScript lo corre al mes siguiente: se descarta.
  const coincide =
    fecha.getFullYear() === anio && fecha.getMonth() === mes && fecha.getDate() === dia
  return coincide ? fecha : undefined
}

/** "2026-10-26" → "lunes, 26 de octubre de 2026" (vacío si no es una fecha válida). */
export function fechaEnPalabras(texto: string): string {
  const fecha = deTextoFecha(texto)
  if (!fecha) return ''
  return new Intl.DateTimeFormat('es-AR', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  }).format(fecha)
}
