import { apiFetch, apiFetchPagina } from '../api/client'
import { conPaginacion, traerTodas } from '../api/paginacion'
import type { PrecioPrevio } from './cotizacionesCalculos'
import type {
  CalificacionProveedorResponseDto,
  CrearCotizacionRequestDto,
  CrearLicitacionRequestDto,
  CotizacionResponseDto,
  CriteriosScoringDto,
  LicitacionResponseDto,
  MiCotizacionDto,
  OrdenCompraAdjudicadaDto,
  OrdenCompraListadoDto,
  ReputacionProveedorDto,
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

/** Datos opcionales del despacho (#216): cuándo llega y una nota. */
export interface DatosDespacho {
  /** Fecha estimada de llegada, en ISO 8601 (no puede ser anterior a hoy). */
  fechaEstimadaLlegada?: string
  /** Observación de hasta 200 caracteres. */
  observacion?: string
}

/**
 * El Proveedor marca su orden de compra como despachada y se avisa a Recepción (OrdenCompraDespachadaEvent).
 * Fuente: POST /api/ordenes-compra/{id}/despachar (solo el Proveedor dueño de la orden; Compras no puede).
 * Es idempotente: si ya estaba despachada responde 200 con los datos originales.
 * Errores: 400 fecha pasada u observación larga, 403 no es su orden, 404 la orden no existe.
 */
export function despacharOrdenCompra(id: string, datos: DatosDespacho = {}): Promise<OrdenCompraListadoDto> {
  const hayDatos = Boolean(datos.fechaEstimadaLlegada || datos.observacion)
  return apiFetch<OrdenCompraListadoDto>(`/api/ordenes-compra/${encodeURIComponent(id)}/despachar`, {
    method: 'POST',
    ...(hayDatos ? { body: JSON.stringify(datos) } : {}),
  })
}

/**
 * Extiende el plazo de cotización de una licitación abierta.
 * Fuente: POST /api/licitaciones/{id}/extender (Comprador, Admin). 400 si la fecha no es posterior, 409 si ya no está abierta.
 */
export function extenderLicitacion(id: string, fechaLimiteCotizacion: string): Promise<LicitacionResponseDto> {
  return apiFetch<LicitacionResponseDto>(`/api/licitaciones/${encodeURIComponent(id)}/extender`, {
    method: 'POST',
    body: JSON.stringify({ fechaLimiteCotizacion }),
  })
}

/**
 * Cierra una licitación abierta sin adjudicar (cierra también sus Magic Links pendientes).
 * Fuente: POST /api/licitaciones/{id}/cerrar (Comprador, Admin). El motivo es obligatorio (hasta 500 caracteres).
 */
export function cerrarLicitacion(id: string, motivo: string): Promise<LicitacionResponseDto> {
  return apiFetch<LicitacionResponseDto>(`/api/licitaciones/${encodeURIComponent(id)}/cerrar`, {
    method: 'POST',
    body: JSON.stringify({ motivo }),
  })
}

/**
 * Último precio unitario pagado por cada artículo (según las órdenes de compra emitidas).
 * Fuente: GET /api/ordenes-compra/precios-historicos?articuloIds=… (Comprador, Admin). Un artículo que nunca se compró no aparece.
 */
export function obtenerPreciosHistoricos(articuloIds: string[]): Promise<PrecioPrevio[]> {
  if (articuloIds.length === 0) return Promise.resolve([])
  const consulta = articuloIds.map((id) => `articuloIds=${encodeURIComponent(id)}`).join('&')
  return apiFetch<PrecioPrevio[]>(`/api/ordenes-compra/precios-historicos?${consulta}`)
}

/**
 * Las cotizaciones que envió el Proveedor que inició sesión, con su resultado.
 * Fuente: GET /api/cotizaciones/mias (Proveedor, Admin)
 */
export function obtenerMisCotizaciones(): Promise<MiCotizacionDto[]> {
  return traerTodas((pagina) =>
    apiFetchPagina<MiCotizacionDto>(conPaginacion('/api/cotizaciones/mias', pagina)),
  )
}

/**
 * Corrige una cotización propia mientras la licitación siga abierta y dentro de plazo.
 * Fuente: PUT /api/cotizaciones/{id} (Proveedor dueño, Admin). Errores: 400 (cerrada/vencida/ítems), 403, 404.
 */
export function corregirCotizacion(
  cotizacionId: string,
  dto: CrearCotizacionRequestDto,
): Promise<CotizacionResponseDto> {
  return apiFetch<CotizacionResponseDto>(`/api/cotizaciones/${encodeURIComponent(cotizacionId)}`, {
    method: 'PUT',
    body: JSON.stringify(dto),
  })
}

/**
 * La calificación vigente de un proveedor. El Proveedor puede pedir solo la suya.
 * Fuente: GET /api/proveedores/{id}/calificacion. 404 si todavía nadie lo calificó.
 */
export function obtenerCalificacionProveedor(proveedorId: string): Promise<CalificacionProveedorResponseDto> {
  return apiFetch<CalificacionProveedorResponseDto>(
    `/api/proveedores/${encodeURIComponent(proveedorId)}/calificacion`,
  )
}

/**
 * Reputación del Proveedor autenticado: promedio, cantidad y últimos comentarios recibidos (#222).
 * Fuente: GET /api/cotizaciones/proveedores/mi-reputacion (solo Proveedor).
 * Sin calificaciones responde 200 con calificacion null, cantidadCalificaciones 0 y comentarios vacíos.
 */
export function obtenerMiReputacion(): Promise<ReputacionProveedorDto> {
  return apiFetch<ReputacionProveedorDto>('/api/cotizaciones/proveedores/mi-reputacion')
}
