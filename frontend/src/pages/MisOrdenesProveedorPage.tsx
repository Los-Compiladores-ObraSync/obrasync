import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { listarOrdenesCompra } from '../licitaciones/licitacionesService'
import type { OrdenCompraListadoDto } from '../licitaciones/licitacionesTipos'
import { fechaLarga } from '../requisiciones/formatoFecha'
import styles from './MisCotizacionesPage.module.css'

/** Portal del Proveedor: las órdenes de compra que ganó y si ya las despachó el comprador. El servicio solo le devuelve las suyas. */
export function MisOrdenesProveedorPage() {
  const [ordenes, setOrdenes] = useState<OrdenCompraListadoDto[]>([])
  const [cargando, setCargando] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelado = false
    listarOrdenesCompra()
      .then((datos) => {
        if (!cancelado) setOrdenes(datos)
      })
      .catch(() => {
        if (!cancelado) setError('No se pudieron cargar tus órdenes. Intentá nuevamente.')
      })
      .finally(() => {
        if (!cancelado) setCargando(false)
      })
    return () => {
      cancelado = true
    }
  }, [])

  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to="/proveedores" className={styles.volver}>
        ← Volver a licitaciones
      </Link>
      <h1>Mis órdenes de compra</h1>
      <p className="intro">Las órdenes que ganaste y su estado de entrega.</p>

      {cargando && <p role="status">Cargando…</p>}
      {!cargando && error && <p role="alert">{error}</p>}
      {!cargando && !error && ordenes.length === 0 && <p>Todavía no tenés órdenes de compra.</p>}

      <ul className={styles.lista}>
        {ordenes.map((o) => (
          <li key={o.id} className={styles.tarjeta}>
            <h2>Orden {o.id.slice(0, 8)}</h2>
            <p className={`${styles.resultado} ${o.fueDespachada ? styles.gano : styles.enviada}`}>
              {o.fueDespachada ? 'Despachada' : 'Emitida, falta despachar'}
            </p>
            <p className={styles.detalle}>
              Emitida el {fechaLarga(o.fechaEmision)}
              {o.fueDespachada && o.fechaDespacho ? ` · Despachada el ${fechaLarga(o.fechaDespacho)}` : ''}
            </p>
          </li>
        ))}
      </ul>
    </section>
  )
}
