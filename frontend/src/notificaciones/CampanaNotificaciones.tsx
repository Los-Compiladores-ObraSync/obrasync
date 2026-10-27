import styles from './CampanaNotificaciones.module.css'

interface Props {
  /** Cantidad de avisos sin leer. Con 0 (o menos) no se muestra el número. */
  cantidad?: number
}

/** Texto que escucha un lector de pantalla: el ícono solo no dice nada. */
function textoAccesible(cantidad: number): string {
  if (cantidad <= 0) return 'Notificaciones: sin avisos'
  return `Notificaciones: ${cantidad} sin leer`
}

/** Campana del encabezado: ícono y contador (badge). Por ahora solo muestra
 *  la cantidad que le pasan; de dónde sale ese número lo resuelve #48 y el
 *  panel que se abre al tocarla, #49. */
export function CampanaNotificaciones({ cantidad = 0 }: Props) {
  const hayAvisos = cantidad > 0

  return (
    <button
      type="button"
      className={styles.campana}
      aria-label={textoAccesible(cantidad)}
    >
      <svg
        className={styles.icono}
        viewBox="0 0 24 24"
        width="26"
        height="26"
        fill="none"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
        focusable="false"
      >
        <path d="M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9" />
        <path d="M13.7 21a2 2 0 0 1-3.4 0" />
      </svg>
      {hayAvisos && (
        <span className={styles.contador} aria-hidden="true">
          {cantidad > 99 ? '99+' : cantidad}
        </span>
      )}
    </button>
  )
}
