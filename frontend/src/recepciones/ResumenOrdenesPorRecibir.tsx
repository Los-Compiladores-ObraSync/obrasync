import { Link } from 'react-router'
import styles from '../notificaciones/ResumenAprobacion.module.css'
import { textoPedidosParaRecibir } from '../notificaciones/textos'
import type { OrdenesPorRecibir } from './useOrdenesPorRecibir'

const RUTA_RECEPCION = '/obra/recepcion'

/** Tarjeta de la pantalla principal del Capataz: cuántos pedidos ya despachó el Proveedor y esperan
 *  que los reciba. Solo aparece cuando hay algo para recibir; si no hay (o no se pudo saber) no ocupa lugar. */
export function ResumenOrdenesPorRecibir({ cantidad }: Pick<OrdenesPorRecibir, 'cantidad'>) {
  if (cantidad === null || cantidad <= 0) return null

  return (
    <section className={styles.tarjeta} aria-label="Resumen de pedidos por recibir">
      <p className={styles.titulo}>{textoPedidosParaRecibir(cantidad)}</p>
      <p className={styles.dato}>El Proveedor ya los despachó. Registrá lo que llegue a la obra.</p>
      <Link to={RUTA_RECEPCION} className="accion-grande">
        Ver pedidos para recibir
      </Link>
    </section>
  )
}
