import { Link } from 'react-router'
import styles from '../notificaciones/ResumenAprobacion.module.css'
import { textoOrdenesParaDespachar } from '../notificaciones/textos'
import type { OrdenesPorDespachar } from './useOrdenesPorDespachar'

const RUTA_MIS_ORDENES = '/proveedores/mis-ordenes'

/** Tarjeta de la pantalla principal del Proveedor: cuántas órdenes tiene que despachar. Solo aparece
 *  cuando hay algo para hacer; sin órdenes pendientes (o si no se pudo saber) no ocupa lugar. */
export function ResumenOrdenesPorDespachar({ cantidad }: Pick<OrdenesPorDespachar, 'cantidad'>) {
  if (cantidad === null || cantidad <= 0) return null

  return (
    <section className={styles.tarjeta} aria-label="Resumen de órdenes por despachar">
      <p className={styles.titulo}>{textoOrdenesParaDespachar(cantidad)}</p>
      <p className={styles.dato}>Marcalas como despachadas cuando salga la mercadería, así la obra puede recibirla.</p>
      <Link to={RUTA_MIS_ORDENES} className="accion-grande">
        Despachar órdenes
      </Link>
    </section>
  )
}
