import { Link } from 'react-router'
import { useAuth } from '../auth/useAuth'
import { ListaLicitaciones } from '../licitaciones/ListaLicitaciones'
import styles from './AprobarRequisicionesPage.module.css'

export function ProveedoresPage() {
  const { usuario } = useAuth()
  if (usuario?.rol !== 'Proveedor') {
    return (
      <section className={`${styles.contenedor} pantalla-ancha`}>
        <Link to="/" className={styles.volver}>← Volver al inicio</Link>
        <h1>Licitaciones abiertas</h1>
        <p>Las cotizaciones solo están disponibles para proveedores.</p>
      </section>
    )
  }
  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to="/" className={styles.volver}>
        ← Volver al inicio
      </Link>
      <h1>Licitaciones abiertas</h1>

      <ListaLicitaciones />
    </section>
  )
}
