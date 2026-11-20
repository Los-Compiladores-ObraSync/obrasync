import { apiFetch } from '../api/client'

/** Tipos sincronizados con SeguimientoRequisicionDto de contracts/openapi/licitaciones-cotizaciones-openapi.json. */
export interface SeguimientoOrdenCompra {
  ordenCompraId: string
  estado: string
  fechaEmision: string
  fechaDespacho?: string | null
  proveedorId: string
  proveedorNombre?: string | null
}

export interface SeguimientoRequisicion {
  requisicionId: string
  licitacionId?: string | null
  estadoLicitacion?: string | null
  fechaLimiteCotizacion?: string | null
  cantidadCotizaciones: number
  ordenesCompra: SeguimientoOrdenCompra[]
}

/**
 * Qué pasó con una requisición aprobada del lado de Licitaciones.
 * Fuente: GET /api/licitaciones/seguimiento/{requisicionId} (Capataz, Comprador, Admin).
 * Si todavía no hay licitación responde 200 con licitacionId nulo (no es un error).
 */
export function obtenerSeguimientoLicitacion(requisicionId: string): Promise<SeguimientoRequisicion> {
  return apiFetch<SeguimientoRequisicion>(
    `/api/licitaciones/seguimiento/${encodeURIComponent(requisicionId)}`,
  )
}
