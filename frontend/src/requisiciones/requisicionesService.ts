import { apiFetch, apiFetchPagina } from '../api/client'
import { conPaginacion, traerTodas, type PaginaDe } from '../api/paginacion'
import type {
  InsumoCatalogo,
  CrearRequisicionRequestDto,
  EditarRequisicionRequestDto,
  RequisicionResponseDto,
  ObraResumen,
} from './requisicionesTipos'

/**
 * Obtiene UNA página de los insumos del catálogo de materiales (#172).
 * Fuente: GET /api/catalogo/insumos?search=&soloActivos=&pagina=&tamano=
 */
export function obtenerInsumosCatalogo(
  search?: string,
  soloActivos = true,
  pagina = 1,
): Promise<PaginaDe<InsumoCatalogo>> {
  const params = new URLSearchParams()
  if (search) {
    params.set('search', search)
  }
  if (!soloActivos) {
    params.set('soloActivos', 'false')
  }
  const query = params.toString() ? `?${params.toString()}` : ''
  return apiFetchPagina<InsumoCatalogo>(conPaginacion(`/api/catalogo/insumos${query}`, pagina))
}

/**
 * Lista las obras disponibles con sus etapas.
 * Fuente: GET /api/obras (autorizado para Capataz, Comprador, Admin)
 *
 * Trae TODAS las páginas (#172): las pantallas usan esta lista para mostrar nombres y para los
 * selectores de obra, y una obra que faltara quedaría sin nombre o sin poder elegirse.
 */
export function obtenerObras(): Promise<ObraResumen[]> {
  return traerTodas((pagina) => apiFetchPagina<ObraResumen>(conPaginacion('/api/obras', pagina)))
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
 * Lista UNA página de requisiciones, opcionalmente filtradas por estado y/u obra (#172).
 * Fuente: GET /api/requisiciones (Capataz, Comprador, Admin)
 */
export function listarRequisiciones(
  filtros: { estado?: string; obraId?: string } = {},
  pagina = 1,
): Promise<PaginaDe<RequisicionResponseDto>> {
  const params = new URLSearchParams()
  if (filtros.estado) params.set('estado', filtros.estado)
  if (filtros.obraId) params.set('obraId', filtros.obraId)
  const query = params.toString() ? `?${params.toString()}` : ''
  return apiFetchPagina<RequisicionResponseDto>(conPaginacion(`/api/requisiciones${query}`, pagina))
}

/**
 * Trae una requisición por su id, con sus ítems.
 * Fuente: GET /api/requisiciones/{id} (Capataz, Comprador, Admin). Responde 404 si no existe.
 */
export function obtenerRequisicion(id: string): Promise<RequisicionResponseDto> {
  return apiFetch<RequisicionResponseDto>(`/api/requisiciones/${encodeURIComponent(id)}`)
}

/** PUT /api/requisiciones/{id}: corrige una requisición Pendiente, o edita y reenvía una Rechazada. */
export function editarYReenviarRequisicion(
  id: string,
  dto: EditarRequisicionRequestDto,
): Promise<RequisicionResponseDto> {
  return apiFetch<RequisicionResponseDto>(`/api/requisiciones/${encodeURIComponent(id)}`, {
    method: 'PUT',
    body: JSON.stringify(dto),
  })
}

/**
 * Cancela una requisición que todavía está Pendiente (antes de que Compras la apruebe).
 * Fuente: POST /api/requisiciones/{id}/cancelar (Capataz dueño, Admin)
 */
export function cancelarRequisicion(id: string): Promise<RequisicionResponseDto> {
  return apiFetch<RequisicionResponseDto>(
    `/api/requisiciones/${encodeURIComponent(id)}/cancelar`,
    { method: 'POST' },
  )
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
