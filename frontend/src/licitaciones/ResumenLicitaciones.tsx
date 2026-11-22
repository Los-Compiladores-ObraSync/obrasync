import { Link } from 'react-router'
import styles from '../notificaciones/ResumenAprobacion.module.css'
import { textoLicitacionesParaCotizar } from '../notificaciones/textos'
import type { LicitacionesPorCotizar } from './useLicitacionesPorCotizar'

// Las licitaciones por cotizar están en el Historial de cotizaciones, con el filtro "Pendientes".
const RUTA_LICITACIONES_ABIERTAS = '/proveedores/mis-cotizaciones?estado=pendientes'

/** Tarjeta de la pantalla principal del Proveedor (igual a la del Comprador con sus requisiciones):
 *  cuántas licitaciones abiertas todavía no cotizó y un botón grande para ir a cotizarlas.
 *  Las que ya cotizó no cuentan: no hay nada más que hacer con ellas. El conteo lo calcula
 *  useLicitacionesPorCotizar (lo comparte con el botón del menú). */
export function ResumenLicitaciones({ cantidad, huboError }: LicitacionesPorCotizar) {
  return (
    <section className={styles.tarjeta} aria-label="Resumen de licitaciones">
      {cantidad === null && !huboError && <p role="status">Cargando…</p>}

      {cantidad === null && huboError && (
        <>
          <p role="alert" className={styles.error}>
            No se pudo saber cuántas licitaciones hay para cotizar.
          </p>
          <Link to={RUTA_LICITACIONES_ABIERTAS} className="accion-grande">
            Cotizar licitaciones
          </Link>
        </>
      )}

      {cantidad !== null && cantidad > 0 && (
        <>
          <p className={styles.titulo}>{textoLicitacionesParaCotizar(cantidad)}</p>
          <Link to={RUTA_LICITACIONES_ABIERTAS} className="accion-grande">
            Cotizar licitaciones
          </Link>
        </>
      )}

      {cantidad === 0 && (
        <div role="status">
          <p className={styles.alDia}>Estás al día</p>
          <p className={styles.dato}>No hay licitaciones abiertas esperando tu cotización.</p>
        </div>
      )}
    </section>
  )
}
