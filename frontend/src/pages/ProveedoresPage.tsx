import { Link } from 'react-router'
import { ListaLicitaciones } from '../licitaciones/ListaLicitaciones'
import styles from './AprobarRequisicionesPage.module.css'

export function ProveedoresPage() {
  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to="/" className={styles.volver}>
        ← Volver al inicio
      </Link>
      <h1>Proveedores</h1>
      <p className="intro">Licitaciones abiertas para cotizar materiales.</p>

      <ListaLicitaciones />
    </section>
  )
}
