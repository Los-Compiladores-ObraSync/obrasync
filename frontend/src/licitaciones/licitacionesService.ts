import { apiFetch, apiFetchPagina } from '../api/client'
import { conPaginacion, traerTodas } from '../api/paginacion'
import type {
  CalificacionProveedorResponseDto,
  CrearCotizacionRequestDto,
  CrearLicitacionRequestDto,
  CotizacionResponseDto,
  CriteriosScoringDto,
  LicitacionResponseDto,
  OrdenCompraAdjudicadaDto,
  OrdenCompraListadoDto,
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
 * Lista las cotizaciones cargadas para un pliego de licitación, con su puntaje.
 * Fuente: GET /api/cotizaciones?licitacionId={id} (Comprador, Admin)
 *
 * "pesos" (opcional, #156): cuánto pesa cada criterio en el puntaje. Cada uno tiene que ser 0 o más
 * y la suma mayor que 0; el servicio los normaliza dividiendo por la suma, así que no hace falta que
 * sumen 100. Si se omite, el servicio usa los pesos por defecto de su configuración.
 * Con pesos inválidos el servicio responde 400.
 */
export function listarCotizaciones(
  licitacionId: string,
  pesos?: CriteriosScoringDto,
): Promise<CotizacionResponseDto[]> {
  const params = new URLSearchParams({ licitacionId })
  if (pesos) {
    params.set('pesoPrecio', String(pesos.precio))
    params.set('pesoFlete', String(pesos.flete))
    params.set('pesoPlazo', String(pesos.plazo))
    params.set('pesoCalificacion', String(pesos.calificacion))
  }
  // Trae todas las páginas (#172): comparar ofertas necesita TODAS las cotizaciones del pliego.
  return traerTodas((pagina) =>
    apiFetchPagina<CotizacionResponseDto>(conPaginacion(`/api/cotizaciones?${params}`, pagina)),
  )
}

/**
 * Carga o cambia la calificación de un proveedor (entero de 1 a 5) (#88, #156).
 * Fuente: PUT /api/cotizaciones/proveedores/{proveedorId}/calificacion (Comprador, Admin).
 * Errores: 400 si no es un entero de 1 a 5, 403 si el rol no puede.
 * Cambia el puntaje de las ofertas de ese proveedor: después hay que volver a pedir las cotizaciones.
 */
export function guardarCalificacionProveedor(
  proveedorId: string,
  calificacion: number,
): Promise<CalificacionProveedorResponseDto> {
  return apiFetch<CalificacionProveedorResponseDto>(
    `/api/cotizaciones/proveedores/${encodeURIComponent(proveedorId)}/calificacion`,
    {
      method: 'PUT',
      body: JSON.stringify({ calificacion }),
    },
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

/**
 * Lista las órdenes de compra con su estado de despacho (#205).
 * Fuente: GET /api/ordenes-compra (Comprador, Admin y Proveedor; esta pantalla es solo de Compras).
 */
export function listarOrdenesCompra(): Promise<OrdenCompraListadoDto[]> {
  // Trae todas las páginas (#172): Compras necesita ver todas las órdenes para despacharlas.
  return traerTodas((pagina) =>
    apiFetchPagina<OrdenCompraListadoDto>(conPaginacion('/api/ordenes-compra', pagina)),
  )
}

/**
 * Marca una orden de compra como despachada y avisa a Recepción (OrdenCompraDespachadaEvent).
 * Fuente: POST /api/ordenes-compra/{id}/despachar (Comprador, Admin). No se puede deshacer.
 * Errores: 403 sin permiso, 404 la orden no existe, 409 ya estaba despachada.
 */
export function despacharOrdenCompra(id: string): Promise<OrdenCompraListadoDto> {
  return apiFetch<OrdenCompraListadoDto>(`/api/ordenes-compra/${encodeURIComponent(id)}/despachar`, {
    method: 'POST',
  })
}
