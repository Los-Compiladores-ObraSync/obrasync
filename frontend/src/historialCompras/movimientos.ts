import { traerTodas } from '../api/paginacion'
import { instanteHistorial, diaHistorial } from './fechas'
import { listarLicitaciones, listarOrdenesCompra } from '../licitaciones/licitacionesService'
import type { LicitacionResponseDto, OrdenCompraListadoDto } from '../licitaciones/licitacionesTipos'
import { listarRequisiciones } from '../requisiciones/requisicionesService'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import { obtenerHistorialRecepciones } from '../recepciones/recepcionesService'
import type { RecepcionHistorial } from '../recepciones/recepcionesTipos'

export type TipoMovimiento = 'requisiciones' | 'licitaciones' | 'ordenes' | 'recepciones'

export interface MovimientoCompra {
  clave: string
  tipo: TipoMovimiento
  descripcion: string
  documento: string
  fecha: string
  obraId: string
  href: string
}

export interface ResultadoMovimientos {
  movimientos: MovimientoCompra[]
  errores: string[]
  adjudicacionesSinFecha: number
}

function fechaValida(fecha?: string | null): fecha is string {
  return Boolean(fecha && diaHistorial(fecha) !== null)
}

function movimiento(
  tipo: TipoMovimiento,
  id: string,
  evento: string,
  descripcion: string,
  documento: string,
  fecha: string | null | undefined,
  obraId: string,
  href: string,
): MovimientoCompra | null {
  if (!fechaValida(fecha)) return null
  return { clave: `${tipo}:${id}:${evento}:${fecha}`, tipo, descripcion, documento, fecha, obraId, href }
}

function movimientosRequisicion(req: RequisicionResponseDto): MovimientoCompra[] {
  const resultado: MovimientoCompra[] = []
  const href = `/compras/historial/requisiciones/${encodeURIComponent(req.requisicionId)}`
  const aprobada = movimiento('requisiciones', req.requisicionId, 'aprobada', 'Requisición aprobada', req.numeroRequisicion || req.requisicionId, req.fechaAprobacion, req.obraId, href)
  if (aprobada) resultado.push(aprobada)

  const rechazos = req.historialRechazos ?? []
  for (const rechazo of rechazos) {
    const item = movimiento('requisiciones', req.requisicionId, `rechazada:${rechazo.id}`, 'Requisición rechazada', req.numeroRequisicion || req.requisicionId, rechazo.fechaRechazo, req.obraId, href)
    if (item) resultado.push(item)
  }
  // Las respuestas anteriores al historial de rechazos solo tienen fechaRechazo.
  if (rechazos.length === 0 && req.estado === 'Rechazada') {
    const rechazo = movimiento('requisiciones', req.requisicionId, 'rechazada', 'Requisición rechazada', req.numeroRequisicion || req.requisicionId, req.fechaRechazo, req.obraId, href)
    if (rechazo) resultado.push(rechazo)
  }
  return resultado
}

function movimientosLicitacion(licitacion: LicitacionResponseDto): MovimientoCompra[] {
  const documento = licitacion.numeroRequisicion || licitacion.requisicionId
  const href = `/compras/licitaciones?licitacionId=${encodeURIComponent(licitacion.licitacionId)}`
  const publicada = movimiento('licitaciones', licitacion.licitacionId, 'publicada', 'Licitación publicada', documento, licitacion.fechaCreacion, licitacion.obraId, href)
  // El listado informa el estado Adjudicada, pero no contiene la fecha del evento de adjudicación.
  // No usar fechaCreacion ni la emisión de la orden como sustituto.
  return publicada ? [publicada] : []
}

function movimientosOrden(orden: OrdenCompraListadoDto): MovimientoCompra[] {
  const codigo = orden.id.slice(0, 8).toUpperCase()
  const href = `/compras/ordenes?ordenCompraId=${encodeURIComponent(orden.id)}`
  const resultado: MovimientoCompra[] = []
  const emitida = movimiento('ordenes', orden.id, 'emitida', 'Orden de compra emitida', `Orden ${codigo}`, orden.fechaEmision, orden.obraId, href)
  if (emitida) resultado.push(emitida)
  if (orden.fueDespachada) {
    const despachada = movimiento('ordenes', orden.id, 'despachada', 'Orden de compra despachada', `Orden ${codigo}`, orden.fechaDespacho, orden.obraId, href)
    if (despachada) resultado.push(despachada)
  }
  return resultado
}

function movimientosRecepcion(ordenId: string, obraId: string, recepciones: RecepcionHistorial[]): MovimientoCompra[] {
  const resultado: MovimientoCompra[] = []
  for (const recepcion of recepciones) {
    if (!recepcion.id) continue
    const href = `/compras/ordenes?ordenCompraId=${encodeURIComponent(ordenId)}`
    const problema = recepcion.estado === 4
    if (!problema && recepcion.estado !== 3) continue
    const descripcion = problema ? 'Entrega con problema' : 'Recepción completa'
    const documento = recepcion.numeroRemito || (problema ? `Problema ${recepcion.id.slice(0, 8).toUpperCase()}` : `Recepción ${recepcion.id.slice(0, 8).toUpperCase()}`)
    // createdAt fecha el registro del evento; fechaRecepcion puede ser una fecha declarada en el remito.
    const fecha = fechaValida(recepcion.createdAt) ? recepcion.createdAt : recepcion.fechaRecepcion
    const item = movimiento('recepciones', recepcion.id, problema ? 'problema' : 'completa', descripcion, documento, fecha, recepcion.obraId ?? obraId, href)
    if (item) resultado.push(item)
  }
  return resultado
}

async function conLimite<T, R>(items: T[], limite: number, procesar: (item: T) => Promise<R>): Promise<R[]> {
  const resultados = new Array<R>(items.length)
  let siguiente = 0
  async function trabajador() {
    while (true) {
      const indice = siguiente++
      if (indice >= items.length) return
      resultados[indice] = await procesar(items[indice])
    }
  }
  await Promise.all(Array.from({ length: Math.min(limite, items.length) }, () => trabajador()))
  return resultados
}

export async function cargarMovimientosCompras(): Promise<ResultadoMovimientos> {
  const errores: string[] = []
  const [requisicionesR, licitacionesR, ordenesR] = await Promise.allSettled([
    traerTodas((pagina) => listarRequisiciones({}, pagina)),
    listarLicitaciones(),
    listarOrdenesCompra(),
  ])
  const requisiciones = requisicionesR.status === 'fulfilled' ? requisicionesR.value : (errores.push('requisiciones'), [])
  const licitaciones = licitacionesR.status === 'fulfilled' ? licitacionesR.value : (errores.push('licitaciones'), [])
  const ordenes = ordenesR.status === 'fulfilled' ? ordenesR.value : (errores.push('órdenes de compra'), [])

  const historiales = await conLimite(ordenes, 4, async (orden) => {
    try {
      return { orden, recepciones: await obtenerHistorialRecepciones(orden.id) }
    } catch {
      errores.push(`recepciones de orden ${orden.id.slice(0, 8)}`)
      return { orden, recepciones: [] as RecepcionHistorial[] }
    }
  })

  const movimientos = [
    ...requisiciones.flatMap(movimientosRequisicion),
    ...licitaciones.flatMap(movimientosLicitacion),
    ...ordenes.flatMap(movimientosOrden),
    ...historiales.flatMap(({ orden, recepciones }) => movimientosRecepcion(orden.id, orden.obraId, recepciones)),
  ]
  const deduplicados = [...new Map(movimientos.map((item) => [item.clave, item])).values()]
  return {
    movimientos: deduplicados.sort((a, b) => instanteHistorial(b.fecha) - instanteHistorial(a.fecha)),
    errores,
    adjudicacionesSinFecha: licitaciones.filter((l) => l.estado === 'Adjudicada').length,
  }
}
