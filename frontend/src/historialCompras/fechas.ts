/** El historial usa el calendario de obra de Buenos Aires en filtros, orden visible y etiquetas. */
export const ZONA_HORARIA_HISTORIAL = 'America/Argentina/Buenos_Aires'

const PATRON_DIA = /^(\d{4})-(\d{2})-(\d{2})$/
const PATRON_ISO = /^(\d{4}-\d{2}-\d{2})(?:T(.*))?$/

export function esDiaReal(value: string | null | undefined): value is string {
  if (!value) return false
  const partes = PATRON_DIA.exec(value)
  if (!partes) return false
  const anio = Number(partes[1])
  const mes = Number(partes[2])
  const dia = Number(partes[3])
  if (anio < 1 || mes < 1 || mes > 12 || dia < 1) return false
  const bisiesto = anio % 4 === 0 && (anio % 100 !== 0 || anio % 400 === 0)
  const diasPorMes = [31, bisiesto ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
  const diasDelMes = diasPorMes[mes - 1]
  return dia <= diasDelMes
}

function fechaParseable(value: string): Date | null {
  const coincidencia = PATRON_ISO.exec(value)
  if (!coincidencia || !esDiaReal(coincidencia[1])) return null
  const hora = coincidencia[2]
  if (hora === undefined) return new Date(`${coincidencia[1]}T12:00:00-03:00`)

  // Los date-time sin zona se interpretan como hora local de Buenos Aires (UTC−03:00),
  // en vez de depender de la zona configurada en el navegador donde corre el frontend.
  const conZona = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(hora) ? value : `${value}-03:00`
  const fecha = new Date(conZona)
  return Number.isFinite(fecha.getTime()) ? fecha : null
}

export function diaHistorial(value: string | null | undefined): string | null {
  if (!value) return null
  if (esDiaReal(value)) return value
  const fecha = fechaParseable(value)
  if (!fecha) return null
  const partes = new Intl.DateTimeFormat('en-CA', {
    timeZone: ZONA_HORARIA_HISTORIAL,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(fecha)
  const valores = Object.fromEntries(partes.map((parte) => [parte.type, parte.value]))
  return `${valores.year}-${valores.month}-${valores.day}`
}

export function instanteHistorial(value: string): number {
  const fecha = fechaParseable(value)
  return fecha?.getTime() ?? Number.NEGATIVE_INFINITY
}

export function fechaVisibleHistorial(value: string): string {
  const dia = diaHistorial(value)
  if (!dia) return 'Fecha no disponible'
  // Mediodía en la zona elegida evita conversiones de día por DST al formatear un valor date-only.
  const fecha = new Date(`${dia}T12:00:00-03:00`)
  return new Intl.DateTimeFormat('es-AR', {
    timeZone: ZONA_HORARIA_HISTORIAL,
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  }).format(fecha)
}
