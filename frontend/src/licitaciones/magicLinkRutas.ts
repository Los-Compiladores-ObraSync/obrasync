/**
 * Dirección de la pantalla pública donde el proveedor abre su Magic Link (#235).
 * Va en un solo lugar para que la pantalla de Compras (#252) que arma el enlace y la ruta pública de la #235
 * usen siempre el mismo camino.
 */
export const RUTA_PUBLICA_MAGIC_LINK = '/cotizar'

/**
 * Enlace completo que Compras le pasa al proveedor: la dirección de la app + la ruta pública + el token.
 * Ojo: NO usamos el campo "url" de la respuesta del backend, porque es la ruta de la API
 * (/api/licitaciones/magic-links/{token}/cotizaciones) y no una pantalla que el proveedor pueda abrir.
 */
export function urlPublicaMagicLink(token: string, origen: string = window.location.origin): string {
  return `${origen}${RUTA_PUBLICA_MAGIC_LINK}/${encodeURIComponent(token)}`
}
