import { fechaLarga } from '../requisiciones/formatoFecha'

/** 1 → "1 hora", 2 → "2 horas", 0.5 → "30 minutos", 1.5 → "1,5 horas". */
export function textoVigencia(horas: number): string {
  if (horas < 1) {
    const minutos = Math.max(1, Math.round(horas * 60))
    return `${minutos} ${minutos === 1 ? 'minuto' : 'minutos'}`
  }
  if (Number.isInteger(horas)) return `${horas} ${horas === 1 ? 'hora' : 'horas'}`
  return `${String(Math.round(horas * 10) / 10).replace('.', ',')} horas`
}

/** "hoy a las 18:30" si vence hoy; si no, "el 21 de noviembre de 2026 a las 18:30". */
export function textoVencimiento(iso: string): string {
  const fecha = new Date(iso)
  if (Number.isNaN(fecha.getTime())) return ''
  const hora = new Intl.DateTimeFormat('es-AR', { hour: '2-digit', minute: '2-digit', hour12: false }).format(fecha)
  const hoy = new Date()
  const esHoy =
    fecha.getFullYear() === hoy.getFullYear() && fecha.getMonth() === hoy.getMonth() && fecha.getDate() === hoy.getDate()
  return esHoy ? `hoy a las ${hora}` : `el ${fechaLarga(iso)} a las ${hora}`
}
