import styles from '../pages/CotizarLicitacionPage.module.css'
import { formatearPlazoEntrega, PLAZOS_ENTREGA_FRECUENTES } from './plazoEntrega'

interface Props {
  /** Lo que hay escrito hoy en el campo de plazo (para marcar la opción elegida). */
  valor: string
  /** Se tocó una opción: el plazo en días. */
  onElegir: (dias: number) => void
  deshabilitado?: boolean
}

/**
 * Plazos de entrega frecuentes para elegir con un toque (#242): evita escribir a mano y los valores raros.
 * El campo de texto sigue disponible para cualquier otro plazo.
 */
export function PlazoEntregaOpciones({ valor, onElegir, deshabilitado = false }: Props) {
  return (
    <div className={styles.opcionesPlazo} role="group" aria-label="Plazos de entrega frecuentes">
      {PLAZOS_ENTREGA_FRECUENTES.map((dias) => {
        const elegido = valor.trim() === String(dias)
        return (
          <button
            key={dias}
            type="button"
            className={elegido ? `${styles.opcionPlazo} ${styles.opcionPlazoActiva}` : styles.opcionPlazo}
            aria-pressed={elegido}
            disabled={deshabilitado}
            onClick={() => onElegir(dias)}
          >
            {formatearPlazoEntrega(dias)}
          </button>
        )
      })}
    </div>
  )
}
