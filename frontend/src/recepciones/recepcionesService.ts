import { apiFetch } from '../api/client'
import type { CrearRecepcionRequest, OrdenPendiente, RecepcionRegistrada } from './recepcionesTipos'

/**
 * Órdenes de compra que todavía no se recibieron por completo.
 * Fuente: GET /api/recepciones/ordenes-pendientes (Capataz, Comprador y Admin).
 */
export function obtenerOrdenesPendientes(): Promise<OrdenPendiente[]> {
  return apiFetch<OrdenPendiente[]>('/api/recepciones/ordenes-pendientes')
}

/**
 * Registra una recepción TOTAL contra una orden de compra, con su remito.
 * Fuente: POST /api/recepciones (solo Capataz y Admin).
 *
 * Errores: 400 si los datos no sirven o si la entrega es parcial (todavía no está disponible);
 * 409 si la orden ya se recibió por completo o el número de remito ya se usó en esa orden.
 */
export function registrarRecepcion(datos: CrearRecepcionRequest): Promise<RecepcionRegistrada> {
  return apiFetch<RecepcionRegistrada>('/api/recepciones', {
    method: 'POST',
    body: JSON.stringify(datos),
  })
}
