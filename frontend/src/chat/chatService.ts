import { apiFetch, apiFetchPagina } from '../api/client'
import { conPaginacion, traerTodas } from '../api/paginacion'
import { listarOrdenesCompra } from '../licitaciones/licitacionesService'
import type {
  EstadoProblema,
  LecturaChatResponseDto,
  MensajeChatOrdenDto,
  ProblemaOrdenEstadoDto,
  ResumenNoLeidosChatDto,
} from './chatTipos'

const base = (ordenCompraId: string) => `/api/ordenes-compra/${encodeURIComponent(ordenCompraId)}`

/**
 * El historial del chat de una orden (mensajes de personas y automáticos), del más antiguo al más nuevo.
 * Fuente: GET /api/ordenes-compra/{id}/chat/mensajes (Comprador, Admin y el Proveedor dueño; otro rol recibe 403).
 */
export function listarMensajes(ordenCompraId: string): Promise<MensajeChatOrdenDto[]> {
  // Trae todas las páginas (#172): la conversación se muestra completa.
  return traerTodas((pagina) =>
    apiFetchPagina<MensajeChatOrdenDto>(conPaginacion(`${base(ordenCompraId)}/chat/mensajes`, pagina)),
  )
}

/**
 * Envía un mensaje al chat de la orden. Texto obligatorio, de 1 a 1000 caracteres.
 * Fuente: POST /api/ordenes-compra/{id}/chat/mensajes → 201 MensajeChatOrdenDto. Errores: 400, 403, 404.
 */
export function enviarMensaje(ordenCompraId: string, texto: string): Promise<MensajeChatOrdenDto> {
  return apiFetch<MensajeChatOrdenDto>(`${base(ordenCompraId)}/chat/mensajes`, {
    method: 'POST',
    body: JSON.stringify({ texto }),
  })
}

/**
 * Marca la conversación como leída para el usuario. Es idempotente.
 * Fuente: POST /api/ordenes-compra/{id}/chat/marcar-leido → 200 LecturaChatResponseDto.
 */
export function marcarLeido(ordenCompraId: string): Promise<LecturaChatResponseDto> {
  return apiFetch<LecturaChatResponseDto>(`${base(ordenCompraId)}/chat/marcar-leido`, { method: 'POST' })
}

/**
 * Marca el problema abierto de la orden como resuelto. Solo Comprador y Admin (Proveedor y Capataz reciben 403).
 * Fuente: POST /api/ordenes-compra/{id}/problema/resolver → 200 ProblemaOrdenEstadoDto. Es idempotente.
 */
export function resolverProblema(ordenCompraId: string): Promise<ProblemaOrdenEstadoDto> {
  return apiFetch<ProblemaOrdenEstadoDto>(`${base(ordenCompraId)}/problema/resolver`, { method: 'POST' })
}

/**
 * Cuántos mensajes sin leer tiene cada orden (solo las que tienen), para el usuario autenticado.
 * Fuente: GET /api/ordenes-compra/chat/no-leidos. Al Proveedor solo le cuenta sus propias órdenes.
 */
export function obtenerNoLeidos(): Promise<ResumenNoLeidosChatDto> {
  return apiFetch<ResumenNoLeidosChatDto>('/api/ordenes-compra/chat/no-leidos')
}

/** Estado de una entrega con problema, tomado de la lista de órdenes (el contrato no tiene un endpoint solo para
 *  esto; el tiempo real lo avisa con ProblemaEstadoActualizado). null si la orden ya no está en la lista. */
export async function obtenerProblemaDeOrden(ordenCompraId: string): Promise<EstadoProblema | null> {
  const ordenes = await listarOrdenesCompra()
  const orden = ordenes.find((o) => o.id === ordenCompraId)
  if (!orden) return null
  return { abierto: orden.tieneProblemaAbierto === true, motivo: orden.motivoProblemaAbierto ?? null }
}
