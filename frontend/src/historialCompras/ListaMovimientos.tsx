import { Link } from 'react-router'
import { fechaVisibleHistorial } from './fechas'
import type { MovimientoCompra } from './movimientos'
import styles from './ListaMovimientos.module.css'

export function ListaMovimientos({ movimientos, nombresObras }: { movimientos: MovimientoCompra[]; nombresObras: Record<string, string> }) {
  if (movimientos.length === 0) return <p role="status">No hay movimientos para mostrar.</p>
  return (
    <ol className={styles.lista}>
      {movimientos.map((item) => (
        <li className={styles.movimiento} key={item.clave}>
          <div>
            <strong>{item.descripcion}</strong>
            <span className={styles.documento}>{item.documento}</span>
          </div>
          <div className={styles.detalles}>
            <span>{nombresObras[item.obraId] ?? `Obra ${item.obraId}`}</span>
            <time dateTime={item.fecha}>{fechaVisibleHistorial(item.fecha)}</time>
            <Link to={item.href} aria-label={`Ver detalle: ${item.descripcion}, ${item.documento}`}>Ver detalle</Link>
          </div>
        </li>
      ))}
    </ol>
  )
}
