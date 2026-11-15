import { hayMas } from '../api/paginacion'
import styles from './BotonVerMas.module.css'

interface BotonVerMasProps {
  /** Filas que ya están en pantalla. */
  cargadas: number
  /** Total que informa el servicio (X-Total-Count). Sin total, el botón no aparece. */
  total: number | undefined
  /** Qué se está contando, para el texto (ej. "insumos"). Opcional. */
  nombre?: string
  cargando: boolean
  /** Falló el último intento de traer más. */
  error: boolean
  onVerMas: () => void
}

/** "Ver más" de los listados paginados (#172). No se dibuja si ya está todo cargado. */
export function BotonVerMas({
  cargadas,
  total,
  nombre,
  cargando,
  error,
  onVerMas,
}: BotonVerMasProps) {
  if (!hayMas(cargadas, total)) return null

  return (
    <div className={styles.contenedor}>
      <p className={styles.resumen} aria-live="polite">
        Mostrando {cargadas} de {total}
        {nombre ? ` ${nombre}` : ''}.
      </p>
      {error && (
        <p className={styles.error} role="alert">
          No se pudieron cargar más. Intentá nuevamente.
        </p>
      )}
      <button type="button" className={styles.boton} onClick={onVerMas} disabled={cargando}>
        {cargando ? 'Cargando…' : 'Ver más'}
      </button>
    </div>
  )
}
