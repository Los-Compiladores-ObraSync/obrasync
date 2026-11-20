import { apiFetch, apiFetchPagina, TIEMPO_LIMITE_ASISTENTE_MS } from '../api/client'
import { conPaginacion, traerTodas } from '../api/paginacion'
import type { CrearRecepcionRequest, LecturaRemito, OrdenPendiente, RecepcionHistorial, RecepcionRegistrada } from './recepcionesTipos'

/**
 * Órdenes de compra que todavía no se recibieron por completo.
 * Fuente: GET /api/recepciones/ordenes-pendientes (Capataz, Comprador y Admin).
 */
export function obtenerOrdenesPendientes(): Promise<OrdenPendiente[]> {
  // Trae todas las páginas (#172): el Capataz elige la orden entre todas las pendientes.
  return traerTodas((pagina) =>
    apiFetchPagina<OrdenPendiente>(conPaginacion('/api/recepciones/ordenes-pendientes', pagina)),
  )
}

/**
 * Registra una recepción total o parcial contra una orden de compra, con su remito.
 * Fuente: POST /api/recepciones (solo Capataz y Admin).
 *
 * Errores: 400 si los datos no sirven o se excede el saldo;
 * 409 si la orden ya se recibió por completo o el número de remito ya se usó en esa orden.
 */
export function registrarRecepcion(datos: CrearRecepcionRequest): Promise<RecepcionRegistrada> {
  return apiFetch<RecepcionRegistrada>('/api/recepciones', {
    method: 'POST',
    body: JSON.stringify(datos),
  })
}

/** Historial completo de recepciones de una orden, respetando la paginación del API. */
export function obtenerHistorialRecepciones(ordenCompraId: string): Promise<RecepcionHistorial[]> {
  return traerTodas((pagina) =>
    apiFetchPagina<RecepcionHistorial>(conPaginacion(`/api/recepciones?ordenCompraId=${encodeURIComponent(ordenCompraId)}`, pagina)),
  )
}

/**
 * Lee el número y la fecha de la foto de un remito (#209).
 * Fuente: POST /api/asistente/remito/leer (multipart, campo "imagen"; Capataz y Admin).
 * Devuelve una SUGERENCIA: no guarda nada, y cada campo es null si no se pudo leer.
 *
 * Errores: 400 si la imagen no sirve; 429 si hay demasiadas fotos o el asistente está ocupado;
 * 503 si el asistente no está disponible (se carga a mano).
 */
export function leerRemitoDeFoto(imagen: Blob): Promise<LecturaRemito> {
  const formulario = new FormData()
  formulario.append('imagen', imagen, 'remito.jpg')
  return apiFetch<LecturaRemito>('/api/asistente/remito/leer', {
    method: 'POST',
    body: formulario,
    // El modelo tarda más que un pedido normal (el servidor le da hasta 30 s).
    tiempoLimiteMs: TIEMPO_LIMITE_ASISTENTE_MS,
  })
}
