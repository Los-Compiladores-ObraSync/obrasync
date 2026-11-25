import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { listarCotizaciones, listarLicitaciones, listarOrdenesCompra } from '../licitaciones/licitacionesService'
import { traerTodas } from '../api/paginacion'
import { listarRequisiciones, obtenerObras } from '../requisiciones/requisicionesService'
import { ResumenAprobacion } from '../notificaciones/ResumenAprobacion'
import { usePendientes } from '../notificaciones/usePendientes'
import { InsigniaCantidad } from '../licitaciones/InsigniaCantidad'
import { obtenerOrdenesPendientes } from '../recepciones/recepcionesService'
import { cargarMovimientosCompras, type MovimientoCompra } from '../historialCompras/movimientos'
import { ListaMovimientos } from '../historialCompras/ListaMovimientos'
import styles from './ComprasPage.module.css'

interface ResumenFlujo {
  porLicitar: number
  porAdjudicar: number
  ordenesPendientesSeguimiento: number
  porRecibir: number
}

function cantidadConNombre(cantidad: number, singular: string, plural: string): string {
  return `${cantidad} ${cantidad === 1 ? singular : plural}`
}

export function ComprasPage({
  integrada = false,
  descripcionPendientes,
}: { integrada?: boolean; descripcionPendientes?: string } = {}) {
  const { cantidad: porAprobar, cargado: aprobacionesCargadas } = usePendientes()
  const [flujo, setFlujo] = useState<ResumenFlujo | null>(null)
  const [errorFlujo, setErrorFlujo] = useState(false)
  const [movimientos, setMovimientos] = useState<MovimientoCompra[]>([])
  const [nombresObras, setNombresObras] = useState<Record<string, string>>({})
  const [errorHistorial, setErrorHistorial] = useState(false)
  const [cargandoHistorial, setCargandoHistorial] = useState(true)

  useEffect(() => {
    if (integrada) return
    let cancelado = false
    void cargarMovimientosCompras().then((resultado) => {
      if (cancelado) return
      setMovimientos(resultado.movimientos.slice(0, 10))
      setErrorHistorial(resultado.errores.length > 0)
    }).catch(() => { if (!cancelado) setErrorHistorial(true) })
      .finally(() => { if (!cancelado) setCargandoHistorial(false) })
    void obtenerObras().then((obras) => {
      if (!cancelado) setNombresObras(Object.fromEntries(obras.map((obra) => [obra.id, obra.nombre])))
    }).catch(() => {})
    return () => { cancelado = true }
  }, [integrada])

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
        const ordenesEnSeguimiento = new Set([
          ...ordenes.filter((orden) => !orden.fueDespachada).map((orden) => orden.id),
          ...recepciones.map((orden) => orden.ordenCompraId),
        ])
        if (cancelado) return
        setFlujo({
          porLicitar: aprobadas.filter((r) => !conLicitacion.has(r.requisicionId)).length,
          porAdjudicar: cotizaciones.filter((lista) => lista.length > 0).length,
          ordenesPendientesSeguimiento: ordenesEnSeguimiento.size,
          porRecibir: recepciones.length,
        })
        setErrorFlujo(false)
      } catch {
        if (!cancelado) setErrorFlujo(true)
      }
    }
    void cargar()
    const temporizador = window.setInterval(() => {
      if (!document.hidden) void cargar()
    }, 60_000)
    const alVolver = () => { if (!document.hidden) void cargar() }
    document.addEventListener('visibilitychange', alVolver)
    return () => {
      cancelado = true
      window.clearInterval(temporizador)
      document.removeEventListener('visibilitychange', alVolver)
    }
  }, [])

  // Una etapa "con trabajo" se destaca en el flujo: es donde Compras tiene algo para hacer.
  const pendientePorEtapa = {
    requisicion: aprobacionesCargadas && porAprobar > 0,
    licitacion: flujo !== null && flujo.porLicitar + flujo.porAdjudicar > 0,
    orden: flujo !== null && flujo.ordenesPendientesSeguimiento > 0,
    recepcion: flujo !== null && flujo.porRecibir > 0,
  }

  const tareas = [
    ...(aprobacionesCargadas && porAprobar > 0 ? [{ texto: `${cantidadConNombre(porAprobar, 'requisición', 'requisiciones')} por aprobar`, ruta: '/compras/requisiciones' }] : []),
    ...(flujo && flujo.porLicitar > 0 ? [{ texto: `${cantidadConNombre(flujo.porLicitar, 'requisición aprobada pendiente de licitación', 'requisiciones aprobadas pendientes de licitación')}`, ruta: '/compras/requisiciones/historial' }] : []),
    ...(flujo && flujo.porAdjudicar > 0 ? [{ texto: `${cantidadConNombre(flujo.porAdjudicar, 'licitación con cotizaciones para adjudicar', 'licitaciones con cotizaciones para adjudicar')}`, ruta: '/compras/licitaciones' }] : []),
    ...(flujo && flujo.ordenesPendientesSeguimiento > 0 ? [{ texto: `${cantidadConNombre(flujo.ordenesPendientesSeguimiento, 'orden pendiente de seguimiento', 'órdenes pendientes de seguimiento')}`, ruta: '/compras/ordenes' }] : []),
  ].slice(0, 4)

  return (
    <section>
      {!integrada && <>
        <h1>Gestión y Compras</h1>
        <p className="intro">Cargá obras y aprobá los pedidos de materiales.</p>
        <ResumenAprobacion />
      </>}

      <section className={styles.historial} aria-label={integrada ? 'Historial de actividad' : 'Lo último que pasó'}>
        <div className={styles.encabezadoHistorial}>
          <h2>{integrada ? 'Historial de actividad' : 'Lo último que pasó'}</h2>
          <Link className={`accion-secundaria ${styles.botonHistorial}`} to="/compras/historial">
            {integrada ? 'Ver historial' : 'Ver todo'}
          </Link>
        </div>
        {integrada ? (
          <p>Consultá las últimas novedades de Compras en el historial completo.</p>
        ) : (
          <>
            {cargandoHistorial && <p role="status">Cargando movimientos…</p>}
            {errorHistorial && <p role="status">Algunos movimientos no pudieron cargarse; se muestran los datos disponibles.</p>}
            {!cargandoHistorial && <ListaMovimientos movimientos={movimientos} nombresObras={nombresObras} />}
          </>
        )}
      </section>

      <section aria-label="Progreso del flujo de compras">
        <h2>Progreso del flujo</h2>
        {errorFlujo ? <p role="status">No se pudieron actualizar todas las etapas del flujo.</p> : (
          <ol className={styles.progreso}>
            <li className={styles.etapa} data-pendiente={pendientePorEtapa.requisicion}><Link className={styles.enlaceEtapa} to="/compras/requisiciones"><span className={styles.etiqueta}>Requisición</span>{' '}<span className={styles.cantidad}>{aprobacionesCargadas ? cantidadConNombre(porAprobar, 'por aprobar', 'por aprobar') : 'Cargando cantidad…'}</span></Link></li>
            <li className={styles.etapa} data-pendiente={pendientePorEtapa.licitacion}><Link className={styles.enlaceEtapa} to="/compras/licitaciones"><span className={styles.etiqueta}>Licitación</span>{' '}<span className={styles.cantidad}>{flujo ? `${cantidadConNombre(flujo.porLicitar, 'por licitar', 'por licitar')} · ${cantidadConNombre(flujo.porAdjudicar, 'por adjudicar', 'por adjudicar')}` : 'Cargando cantidad…'}</span></Link></li>
            <li className={styles.etapa} data-pendiente={pendientePorEtapa.orden}><Link className={styles.enlaceEtapa} to="/compras/ordenes"><span className={styles.etiqueta}>Orden de compra</span>{' '}<span className={styles.cantidad}>{flujo ? cantidadConNombre(flujo.ordenesPendientesSeguimiento, 'pendiente de seguimiento', 'pendientes de seguimiento') : 'Cargando cantidad…'}</span></Link></li>
            <li className={styles.etapa} data-pendiente={pendientePorEtapa.recepcion}><Link className={styles.enlaceEtapa} to="/compras/recepcion"><span className={styles.etiqueta}>Recepción</span>{' '}<span className={styles.cantidad}>{flujo ? cantidadConNombre(flujo.porRecibir, 'orden pendiente de recibir', 'órdenes pendientes de recibir') : 'Cargando cantidad…'}</span></Link></li>
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
        <li><Link to="/compras/requisiciones/historial" className="accion-secundaria">{aprobacionesCargadas && porAprobar > 0 ? <span>Historial de requisiciones<InsigniaCantidad cantidad={porAprobar} descripcion={descripcionPendientes ?? `${porAprobar} requisiciones pendientes de aprobación`} /></span> : 'Historial de requisiciones'}</Link></li>
        <li><Link to="/compras/licitaciones" className="accion-secundaria">Licitaciones y cotizaciones</Link></li>
        <li><Link to="/compras/ordenes" className="accion-secundaria">Órdenes de compra</Link></li>
        <li><Link to="/compras/presupuesto" className="accion-secundaria">Presupuesto</Link></li>
        <li><Link to="/compras/obras" className="accion-secundaria">Ver obras</Link></li>
        <li><Link to="/compras/insumos/carga" className="accion-secundaria">Cargar insumos (CSV)</Link></li>
        {!integrada && <li><Link to="/" className="accion-secundaria">Volver al inicio</Link></li>}
      </ul>
    </section>
  )
}
