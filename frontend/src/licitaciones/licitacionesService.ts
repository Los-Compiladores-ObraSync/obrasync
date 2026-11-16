import { apiFetch, apiFetchPagina } from '../api/client'
import { conPaginacion, traerTodas } from '../api/paginacion'
import type {
  CrearCotizacionRequestDto,
  CrearLicitacionRequestDto,
  CotizacionResponseDto,
  LicitacionResponseDto,
  OrdenCompraAdjudicadaDto,
} from './licitacionesTipos'

/**
 * Lista las licitaciones disponibles. Al Proveedor el backend ya le devuelve solo las abiertas.
 * Fuente: GET /api/licitaciones (Comprador, Proveedor, Admin)
 */
export function listarLicitaciones(): Promise<LicitacionResponseDto[]> {
  // Trae todas las páginas (#172): la pantalla muestra la lista completa de pliegos.
  return traerTodas((pagina) =>
    apiFetchPagina<LicitacionResponseDto>(conPaginacion('/api/licitaciones', pagina)),
  )
}

/**
 * Obtiene un pliego de licitación por su id, con sus ítems.
 * Fuente: GET /api/licitaciones/{id} (Comprador, Proveedor, Admin). Responde 404 si no existe.
 */
export function obtenerLicitacion(id: string): Promise<LicitacionResponseDto> {
  return apiFetch<LicitacionResponseDto>(`/api/licitaciones/${encodeURIComponent(id)}`)
}

/**
 * Publica el pliego de una requisición aprobada, con el plazo para cotizar.
 * Fuente: POST /api/licitaciones (Comprador, Admin). 404 si la requisición no existe o no está
 * aprobada, 400 si la fecha límite no es futura, 409 si ya hay una licitación abierta para ella.
 */
export function publicarLicitacion(
  dto: CrearLicitacionRequestDto,
): Promise<LicitacionResponseDto> {
  return apiFetch<LicitacionResponseDto>('/api/licitaciones', {
    method: 'POST',
    body: JSON.stringify(dto),
  })
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

/**
 * Lista las cotizaciones cargadas para un pliego de licitación.
 * Fuente: GET /api/cotizaciones?licitacionId={id} (Comprador, Admin)
 */
export function listarCotizaciones(licitacionId: string): Promise<CotizacionResponseDto[]> {
  // Trae todas las páginas (#172): comparar ofertas necesita TODAS las cotizaciones del pliego.
  return traerTodas((pagina) =>
    apiFetchPagina<CotizacionResponseDto>(
      conPaginacion(`/api/cotizaciones?licitacionId=${encodeURIComponent(licitacionId)}`, pagina),
    ),
  )
}

/**
 * Adjudica una licitación a una cotización específica y emite la Orden de Compra.
 * Fuente: POST /api/licitaciones/{licitacionId}/adjudicar (Comprador, Admin)
 */
export function adjudicarLicitacion(
  licitacionId: string,
  cotizacionId: string,
): Promise<OrdenCompraAdjudicadaDto> {
  return apiFetch<OrdenCompraAdjudicadaDto>(
    `/api/licitaciones/${encodeURIComponent(licitacionId)}/adjudicar`,
    {
      method: 'POST',
      body: JSON.stringify({ cotizacionId }),
    },
  )
}

