import { Link } from 'react-router'
import styles from './ResumenAprobacion.module.css'
import { textoPendientes } from './textos'
import { usePendientes } from './usePendientes'

/** Tarjeta de la pantalla principal del Comprador: cuántas requisiciones esperan su
 *  aprobación y un botón grande para ir a aprobarlas. Usa la misma lista que la campana
 *  (PendientesProvider), así no se pide dos veces. */
export function ResumenAprobacion() {
  const { cantidad, cargado, huboError } = usePendientes()

  return (
    <section className={styles.tarjeta} aria-label="Resumen de aprobaciones">
      {!cargado && !huboError && <p role="status">Cargando…</p>}

      {!cargado && huboError && (
        <>
          <p role="alert" className={styles.error}>
            No se pudo saber cuántas requisiciones esperan.
          </p>
          <Link to="/compras/requisiciones" className="accion-grande">
            Aprobar requisiciones
          </Link>
        </>
      )}

      {cargado && cantidad > 0 && (
        <>
          <p className={styles.titulo}>{textoPendientes(cantidad)}</p>
          <Link to="/compras/requisiciones" className="accion-grande">
            Aprobar requisiciones
          </Link>
        </>
      )}

      {cargado && cantidad === 0 && (
        <div role="status">
          <p className={styles.alDia}>Estás al día</p>
          <p className={styles.dato}>No hay requisiciones esperando tu aprobación.</p>
        </div>
      )}
    </section>
  )
}
