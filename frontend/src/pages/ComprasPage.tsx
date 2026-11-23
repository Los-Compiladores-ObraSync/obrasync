import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { listarCotizaciones, listarLicitaciones, listarOrdenesCompra } from '../licitaciones/licitacionesService'
import { traerTodas } from '../api/paginacion'
import { listarRequisiciones } from '../requisiciones/requisicionesService'
import { ResumenAprobacion } from '../notificaciones/ResumenAprobacion'
import { usePendientes } from '../notificaciones/usePendientes'
import { InsigniaCantidad } from '../licitaciones/InsigniaCantidad'
import { obtenerOrdenesPendientes } from '../recepciones/recepcionesService'

interface ResumenFlujo {
  porLicitar: number
  porAdjudicar: number
  ordenesSinDespachar: number
  porRecibir: number
}

export function ComprasPage() {
  const { cantidad: porAprobar, cargado: aprobacionesCargadas } = usePendientes()
  const [flujo, setFlujo] = useState<ResumenFlujo | null>(null)
  const [errorFlujo, setErrorFlujo] = useState(false)

  useEffect(() => {
    let cancelado = false
    async function cargar() {
      try {
        const [licitaciones, ordenes, recepciones, aprobadas] = await Promise.all([
          listarLicitaciones(), listarOrdenesCompra(), obtenerOrdenesPendientes(),
          traerTodas((pagina) => listarRequisiciones({ estado: 'Aprobada' }, pagina)),
        ])
        const abiertas = licitaciones.filter((l) => l.estado === 'Abierta')
        const cotizaciones = await Promise.all(abiertas.map((l) => listarCotizaciones(l.licitacionId)))
        const conLicitacion = new Set(licitaciones.filter((l) => l.estado === 'Abierta' || l.estado === 'Adjudicada').map((l) => l.requisicionId))
        if (cancelado) return
        setFlujo({
          porLicitar: aprobadas.filter((r) => !conLicitacion.has(r.requisicionId)).length,
          porAdjudicar: cotizaciones.filter((lista) => lista.length > 0).length,
          ordenesSinDespachar: ordenes.filter((o) => !o.fueDespachada).length,
          porRecibir: recepciones.filter((o) => Boolean(o.fechaDespacho)).length,
        })
        setErrorFlujo(false)
      } catch {
        if (!cancelado) setErrorFlujo(true)
      }
    }
    void cargar()
    return () => { cancelado = true }
  }, [])

  const tareas = [
    ...(aprobacionesCargadas && porAprobar > 0 ? [{ texto: `${porAprobar} requisición${porAprobar === 1 ? '' : 'es'} por aprobar`, ruta: '/compras/requisiciones' }] : []),
    ...(flujo && flujo.porLicitar > 0 ? [{ texto: `${flujo.porLicitar} requisición${flujo.porLicitar === 1 ? '' : 'es'} aprobada${flujo.porLicitar === 1 ? '' : 's'} pendiente${flujo.porLicitar === 1 ? '' : 's'} de licitación`, ruta: '/compras/requisiciones/historial' }] : []),
    ...(flujo && flujo.porAdjudicar > 0 ? [{ texto: `${flujo.porAdjudicar} licitación${flujo.porAdjudicar === 1 ? '' : 'es'} con cotizaciones para adjudicar`, ruta: '/compras/licitaciones' }] : []),
    ...(flujo && flujo.ordenesSinDespachar > 0 ? [{ texto: `${flujo.ordenesSinDespachar} orden${flujo.ordenesSinDespachar === 1 ? '' : 'es'} pendiente${flujo.ordenesSinDespachar === 1 ? '' : 's'} de despacho`, ruta: '/compras/ordenes' }] : []),
  ].slice(0, 4)

  return (
    <section>
      <h1>Gestión y Compras</h1>
      <p className="intro">Cargá obras y aprobá los pedidos de materiales.</p>
      <ResumenAprobacion />

      <section aria-label="Progreso del flujo de compras">
        <h2>Progreso del flujo</h2>
        {errorFlujo ? <p role="status">No se pudieron actualizar todas las etapas del flujo.</p> : (
          <ol className="lista-acciones">
            <li><Link className="accion-secundaria" to="/compras/requisiciones">Requisición — {aprobacionesCargadas ? `${porAprobar} por aprobar` : 'cargando'}</Link></li>
            <li><Link className="accion-secundaria" to="/compras/licitaciones">Licitación — {flujo ? `${flujo.porLicitar} por licitar · ${flujo.porAdjudicar} por adjudicar` : 'cargando'}</Link></li>
            <li><Link className="accion-secundaria" to="/compras/ordenes">Orden de compra — {flujo ? flujo.ordenesSinDespachar : '…'} por seguir</Link></li>
            <li><Link className="accion-secundaria" to="/compras/recepcion">Recepción — {flujo ? flujo.porRecibir : '…'} pendientes</Link></li>
          </ol>
        )}
      </section>

      <section aria-label="Qué hago ahora">
        <h2>Qué hago ahora</h2>
        {tareas.length ? <ul className="lista-acciones">{tareas.map((t) => <li key={t.texto}><Link className="accion-secundaria" to={t.ruta}>{t.texto} →</Link></li>)}</ul> : aprobacionesCargadas && flujo && !errorFlujo ? <p role="status">No hay tareas pendientes. El flujo está al día.</p> : <p role="status">Cargando tareas de Compras…</p>}
      </section>

      <h2>Accesos de Compras</h2>
      <ul className="lista-acciones">
        <li><Link to="/compras/requisiciones" className="accion-secundaria">Aprobar requisiciones</Link></li>
        <li><Link to="/compras/requisiciones/historial" className="accion-secundaria">{aprobacionesCargadas && porAprobar > 0 ? <span>Historial de requisiciones<InsigniaCantidad cantidad={porAprobar} descripcion={`${porAprobar} requisiciones pendientes de aprobación`} /></span> : 'Historial de requisiciones'}</Link></li>
        <li><Link to="/compras/licitaciones" className="accion-secundaria">Licitaciones y cotizaciones</Link></li>
        <li><Link to="/compras/ordenes" className="accion-secundaria">Órdenes de compra</Link></li>
        <li><Link to="/compras/presupuesto" className="accion-secundaria">Presupuesto</Link></li>
        <li><Link to="/compras/obras" className="accion-secundaria">Ver obras</Link></li>
        <li><Link to="/compras/insumos/carga" className="accion-secundaria">Cargar insumos (CSV)</Link></li>
        <li><Link to="/" className="accion-secundaria">Volver al inicio</Link></li>
      </ul>
    </section>
  )
}
