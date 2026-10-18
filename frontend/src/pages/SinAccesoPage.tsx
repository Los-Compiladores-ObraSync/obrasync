import { Link } from 'react-router'
import styles from './SinAccesoPage.module.css'

export function SinAccesoPage() {
  return (
    <section>
      <h1>Sin acceso</h1>
      <p>Tu rol no tiene permiso para ver esta sección.</p>
      <Link to="/" className={styles.volver}>
        Volver al inicio
      </Link>
    </section>
  )
}
