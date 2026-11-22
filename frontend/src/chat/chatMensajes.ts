import { ApiError } from '../api/ApiError'
import { MAX_MENSAJE } from './chatConfig'
import type { MensajeChatOrdenDto } from './chatTipos'

/** Une mensajes nuevos con los que ya hay: sin repetidos (por id) y del más antiguo al más nuevo.
 *  El mismo mensaje puede llegar por tres caminos (la respuesta de enviar, el tiempo real y la consulta de respaldo). */
export function unirMensajes(actuales: MensajeChatOrdenDto[], nuevos: MensajeChatOrdenDto[]): MensajeChatOrdenDto[] {
  const porId = new Map(actuales.map((m) => [m.id, m]))
  for (const mensaje of nuevos) porId.set(mensaje.id, mensaje)
  return [...porId.values()].sort((a, b) => Date.parse(a.fechaCreacion) - Date.parse(b.fechaCreacion))
}

/** Devuelve el mensaje de error del texto, o null si se puede enviar. */
export function validarMensaje(texto: string): string | null {
  const limpio = texto.trim()
  if (limpio.length === 0) return 'Escribí un mensaje antes de enviar.'
  if (limpio.length > MAX_MENSAJE) return `El mensaje no puede superar los ${MAX_MENSAJE} caracteres.`
  return null
}

/** Solo Compras (Comprador y Admin) puede marcar un problema como resuelto. */
export function puedeResolverProblema(rol: string | undefined): boolean {
  return rol === 'Comprador' || rol === 'Admin'
}

/** Texto para mostrar cuando falla enviar un mensaje, marcar leído o resolver el problema. */
export function mensajeDeErrorChat(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 400) return err.detail ?? 'Revisá el mensaje.'
    if (err.status === 403) return 'No tenés acceso al chat de esta orden.'
    if (err.status === 404) return err.detail ?? 'La orden de compra ya no existe.'
  }
  return 'No se pudo conectar con el servidor. Intentá nuevamente.'
}

/** "22/11/2026 20:15" (hora local). Vacío si la fecha no es válida. */
export function fechaYHora(iso: string): string {
  const fecha = new Date(iso)
  if (Number.isNaN(fecha.getTime())) return ''
  return new Intl.DateTimeFormat('es-AR', { dateStyle: 'short', timeStyle: 'short' }).format(fecha)
}
