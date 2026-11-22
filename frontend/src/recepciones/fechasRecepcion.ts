import { fechaEnPalabras } from '../obras/fechaEnPalabras'

/** Muestra el día escrito en el date-time, sin convertirlo a la zona local y cambiar el día. */
export function fechaDeRecepcion(fechaRecepcion?: string | null): string {
  if (!fechaRecepcion || !Number.isFinite(Date.parse(fechaRecepcion))) return 'Fecha no disponible'
  const diaISO = /^(\d{4}-\d{2}-\d{2})T/.exec(fechaRecepcion)?.[1]
  if (!diaISO) return 'Fecha no disponible'
  return fechaEnPalabras(diaISO) || 'Fecha no disponible'
}
