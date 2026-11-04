import { apiFetch } from '../api/client'
import type {
  CrearCotizacionRequestDto,
  CotizacionResponseDto,
  LicitacionResponseDto,
} from './licitacionesTipos'

/**
 * Lista las licitaciones disponibles. Al Proveedor el backend ya le devuelve solo las abiertas.
 * Fuente: GET /api/licitaciones (Comprador, Proveedor, Admin)
 */
export function listarLicitaciones(): Promise<LicitacionResponseDto[]> {
  return apiFetch<LicitacionResponseDto[]>('/api/licitaciones')
}

/**
 * Obtiene un pliego de licitación por su id, con sus ítems.
 * Fuente: GET /api/licitaciones/{id} (Comprador, Proveedor, Admin). Responde 404 si no existe.
 */
export function obtenerLicitacion(id: string): Promise<LicitacionResponseDto> {
  return apiFetch<LicitacionResponseDto>(`/api/licitaciones/${encodeURIComponent(id)}`)
}

/**
 * Carga una cotización para un pliego de licitación.
 * Fuente: POST /api/cotizaciones (Proveedor, Admin)
 */
export function crearCotizacion(
  dto: CrearCotizacionRequestDto,
): Promise<CotizacionResponseDto> {
  return apiFetch<CotizacionResponseDto>('/api/cotizaciones', {
    method: 'POST',
    body: JSON.stringify(dto),
  })
}
