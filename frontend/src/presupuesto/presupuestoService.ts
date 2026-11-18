import { apiFetch, apiFetchPagina } from '../api/client'
import { conPaginacion, TAMANO_PAGINA, traerTodas } from '../api/paginacion'
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
 * Lista todas las órdenes bloqueadas. Recepción pagina a 50 y publica el total
 * en X-Total-Count; si ese contrato no se cumple, se falla en vez de mostrar
 * silenciosamente una lista incompleta.
 */
export function listarOrdenesBloqueadas(): Promise<OrdenCompraBloqueada[]> {
  const ruta = '/api/recepciones/ordenes-bloqueadas'
  let totalInformado: number | undefined
  let acumuladas = 0
  const idsVistos = new Set<string>()

  return traerTodas(async (pagina) => {
    const respuesta = await apiFetchPagina<OrdenCompraBloqueada>(
      conPaginacion(ruta, pagina),
      {},
      { exigirTotalValido: true },
    )
    if (respuesta.total === undefined) {
      throw new Error('La respuesta de órdenes bloqueadas no incluye X-Total-Count.')
    }
    if (totalInformado !== undefined && respuesta.total !== totalInformado) {
      throw new Error('El total de órdenes bloqueadas cambió durante la consulta paginada.')
    }
    totalInformado = respuesta.total
    const esperadas = Math.min(TAMANO_PAGINA, Math.max(0, respuesta.total - acumuladas))
    if (respuesta.items.length !== esperadas) {
      throw new Error('La respuesta de órdenes bloqueadas contiene una página incompleta o inconsistente.')
    }
    for (const orden of respuesta.items) {
      if (idsVistos.has(orden.ordenCompraId)) {
        throw new Error('La consulta de órdenes bloqueadas repitió registros entre páginas.')
      }
      idsVistos.add(orden.ordenCompraId)
    }
    acumuladas += respuesta.items.length
    return respuesta
  }).then((ordenes) => {
    if (totalInformado === undefined || ordenes.length !== totalInformado) {
      throw new Error(
        `La consulta de órdenes bloqueadas quedó incompleta o inconsistente (${ordenes.length} de ${totalInformado ?? 'desconocido'}).`,
      )
    }
    return ordenes
  })
}

export { formatearPesos } from './formatoPesos'
