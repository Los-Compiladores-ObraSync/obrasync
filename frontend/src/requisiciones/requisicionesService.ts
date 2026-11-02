import { apiFetch } from '../api/client'
import type {
  InsumoCatalogo,
  CrearRequisicionRequestDto,
  RequisicionResponseDto,
  ObraResumen,
} from './requisicionesTipos'

/**
 * Obtiene los insumos disponibles en el catálogo de materiales.
 * Fuente: GET /api/catalogo/insumos
 */
export function obtenerInsumosCatalogo(
  search?: string,
  soloActivos = true,
): Promise<InsumoCatalogo[]> {
  const params = new URLSearchParams()
  if (search) {
    params.set('search', search)
  }
  if (!soloActivos) {
    params.set('soloActivos', 'false')
  }
  const query = params.toString() ? `?${params.toString()}` : ''
  return apiFetch<InsumoCatalogo[]>(`/api/catalogo/insumos${query}`)
}

/**
 * Lista las obras disponibles con sus etapas.
 * Fuente: GET /api/obras (autorizado para Capataz, Comprador, Admin)
 */
export function obtenerObras(): Promise<ObraResumen[]> {
  return apiFetch<ObraResumen[]>('/api/obras')
}

export function generarIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return `${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}`
}

/**
 * Envía una nueva requisición de materiales.
 * Fuente: POST /api/requisiciones (autorizado para Capataz, Admin)
 */
export function crearRequisicion(
  dto: CrearRequisicionRequestDto,
  idempotencyKey: string = generarIdempotencyKey(),
): Promise<RequisicionResponseDto> {
  return apiFetch<RequisicionResponseDto>('/api/requisiciones', {
    method: 'POST',
    headers: {
      'Idempotency-Key': idempotencyKey,
    },
    body: JSON.stringify(dto),
  })
}
/**
 * Lista requisiciones, opcionalmente filtradas por estado y/u obra.
 * Fuente: GET /api/requisiciones (Capataz, Comprador, Admin)
 */
export function listarRequisiciones(
  filtros: { estado?: string; obraId?: string } = {},
): Promise<RequisicionResponseDto[]> {
  const params = new URLSearchParams()
  if (filtros.estado) params.set('estado', filtros.estado)
  if (filtros.obraId) params.set('obraId', filtros.obraId)
  const query = params.toString() ? `?${params.toString()}` : ''
  return apiFetch<RequisicionResponseDto[]>(`/api/requisiciones${query}`)
}

/**
 * Trae una requisición por su id, con sus ítems.
 * Fuente: GET /api/requisiciones/{id} (Capataz, Comprador, Admin). Responde 404 si no existe.
 */
export function obtenerRequisicion(id: string): Promise<RequisicionResponseDto> {
  return apiFetch<RequisicionResponseDto>(`/api/requisiciones/${encodeURIComponent(id)}`)
}

/**
 * Aprueba una requisición pendiente. Publica RequisicionAprobadaEvent en el backend.
 * Fuente: POST /api/requisiciones/{id}/aprobar (Comprador, Admin)
 */
export function aprobarRequisicion(id: string): Promise<RequisicionResponseDto> {
  return apiFetch<RequisicionResponseDto>(
    `/api/requisiciones/${encodeURIComponent(id)}/aprobar`,
    { method: 'POST' },
  )
}

/**
 * Rechaza una requisición pendiente.
 * Fuente: POST /api/requisiciones/{id}/rechazar (Comprador, Admin)
 */
export function rechazarRequisicion(
  id: string,
  motivo: string,
): Promise<RequisicionResponseDto> {
  return apiFetch<RequisicionResponseDto>(
    `/api/requisiciones/${encodeURIComponent(id)}/rechazar`,
    {
      method: 'POST',
      body: JSON.stringify({ motivo }),
    },
  )
}
