import { apiFetch, apiFetchPagina } from '../api/client'
import { conPaginacion, traerTodas } from '../api/paginacion'
import { estaEnModoSimulado } from '../mocks/modoSimulado'
import type {
  FiltrosPartidas,
  LiberarOrdenCompraRequest,
  OrdenCompraBloqueada,
  OrdenCompraLiberada,
  PartidaPresupuestaria,
} from './presupuestoTipos'

/**
 * Consulta las partidas presupuestarias de una o varias obras, acumulando todas las páginas (#172).
 * Fuente: GET /api/presupuestos/partidas?obraId&etapa&pagina&tamano (Admin, Comprador, Capataz).
 */
export function obtenerPartidas(filtros: FiltrosPartidas = {}): Promise<PartidaPresupuestaria[]> {
  const params = new URLSearchParams()
  if (filtros.obraId) params.append('obraId', filtros.obraId)
  if (filtros.etapa) params.append('etapa', filtros.etapa)
  const query = params.toString()
  const rutaBase = query ? `/api/presupuestos/partidas?${query}` : '/api/presupuestos/partidas'

  return traerTodas((pagina) =>
    apiFetchPagina<PartidaPresupuestaria>(conPaginacion(rutaBase, pagina)),
  )
}

/**
 * Autoriza y libera una orden de compra bloqueada por desvío presupuestario (#96, #97).
 * Exclusivo para el rol Admin.
 * Fuente: POST /api/recepciones/ordenes/{id}/liberar
 *
 * Errores: 400 si los datos son inválidos (ej. motivo > 500 caracteres);
 * 401 si no está autenticado;
 * 403 si el usuario no tiene rol Admin;
 * 404 si la orden no existe;
 * 409 si la orden ya no está Bloqueada.
 */
export function liberarOrdenCompra(
  ordenCompraId: string,
  datos?: LiberarOrdenCompraRequest,
): Promise<OrdenCompraLiberada> {
  return apiFetch<OrdenCompraLiberada>(`/api/recepciones/ordenes/${ordenCompraId}/liberar`, {
    method: 'POST',
    body: JSON.stringify(datos ?? {}),
  })
}

/**
 * PROPUESTO, NO ESTÁ EN EL CONTRATO:
 * Lista las órdenes de compra bloqueadas por desvío presupuestario (#97).
 * Solicitado al squad de Recepción y Control Presupuestario.
 * Con backend real no se llama y devuelve lista vacía ([]); solo se solicita
 * si está en modo simulado (estaEnModoSimulado()).
 * Fuente simulada: GET /api/recepciones/ordenes-bloqueadas
 */
export async function listarOrdenesBloqueadas(): Promise<OrdenCompraBloqueada[]> {
  if (!estaEnModoSimulado()) {
    return []
  }
  return apiFetch<OrdenCompraBloqueada[]>('/api/recepciones/ordenes-bloqueadas')
}

export { formatearPesos } from './formatoPesos'
