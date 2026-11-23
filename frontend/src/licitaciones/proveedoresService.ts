import { apiFetchPagina } from '../api/client'
import { conPaginacion, traerTodas } from '../api/paginacion'

/** Un proveedor activo. Es el item de GET /api/usuarios/proveedores (ProveedorDto del contrato). */
export interface Proveedor {
  id: string
  nombreCompleto: string
}

/**
 * Lista los proveedores activos, para elegir a quién generarle un Magic Link (#157).
 * Fuente: GET /api/usuarios/proveedores (Comprador, Admin)
 */
export function obtenerProveedores(): Promise<Proveedor[]> {
  return traerTodas((pagina) =>
    apiFetchPagina<Proveedor>(conPaginacion('/api/usuarios/proveedores', pagina)),
  )
}
