import { useState } from 'react'
import {
  CRITERIOS_SCORING,
  estrellasDePesos,
  pesosDeEstrellas,
  type ClaveCriterio,
  type EstrellasPesos,
} from './ponderaciones'
import type { CriteriosScoringDto } from './licitacionesTipos'
import styles from './PonderacionesScoring.module.css'

interface Props {
  /** Pesos que el servicio usó en el último cálculo (0 a 1). */
  ponderacionesActuales: CriteriosScoringDto
  /** Si hay pesos propios aplicados (en vez de los de por defecto). */
  hayPesosPropios: boolean
  recalculando: boolean
  /** Mensaje de error del servicio al recalcular (por ejemplo, un 400), si lo hubo. */
  errorServicio: string | null
  alRecalcular: (pesos: CriteriosScoringDto) => void
  alRestablecer: () => void
}

/**
 * Deja al Comprador elegir cuánto pesa cada criterio en el puntaje (#156).
 * Cada criterio se elige con estrellas (0 a 5): lo que importa es la proporción entre ellos, y el
 * servicio los normaliza. Ejemplo: 5 / 2 / 2 / 1 estrellas equivale a 50 / 20 / 20 / 10 %.
 * Tocar de nuevo la estrella elegida deja el criterio en 0 (no cuenta).
 */
export function PonderacionesScoring({
  ponderacionesActuales,
  hayPesosPropios,
  recalculando,
  errorServicio,
  alRecalcular,
  alRestablecer,
}: Props) {
  const [estrellas, setEstrellas] = useState<EstrellasPesos>(() => estrellasDePesos(ponderacionesActuales))
  const [errorLocal, setErrorLocal] = useState<string | null>(null)

  function elegir(clave: ClaveCriterio, valor: number) {
    // Tocar la estrella que ya estaba elegida la apaga (el criterio queda en 0).
    setEstrellas((anteriores) => ({ ...anteriores, [clave]: anteriores[clave] === valor ? 0 : valor }))
    setErrorLocal(null)
  }

  function recalcular() {
    const resultado = pesosDeEstrellas(estrellas)
    if (!resultado.ok) {
      setErrorLocal(resultado.mensaje)
      return
    }
    setErrorLocal(null)
    alRecalcular(resultado.pesos)
  }

  function restablecer() {
    setErrorLocal(null)
    // Los campos vuelven a los de por defecto cuando llegue la respuesta (ver "key" en la pantalla).
    alRestablecer()
  }

  const error = errorLocal ?? errorServicio

  return (
    <section className={styles.contenedor} aria-label="¿Qué buscas en una oferta?">
      <h3 className={styles.titulo}>¿Qué buscas en una oferta?</h3>
      <p className={styles.ayuda}>Elegí con estrellas qué tanto te importa cada criterio.</p>
      <div className={styles.campos}>
        {CRITERIOS_SCORING.map(({ clave, etiqueta }) => (
          <div key={clave} role="group" aria-label={etiqueta} className={styles.campo}>
            <span className={styles.etiqueta}>{etiqueta}</span>
            <div className={styles.estrellas}>
              {[1, 2, 3, 4, 5].map((valor) => (
                <button
                  key={valor}
                  type="button"
                  className={`${styles.estrella} ${valor <= estrellas[clave] ? styles.llena : ''}`}
                  aria-label={`${valor} de 5`}
                  aria-pressed={estrellas[clave] === valor}
                  disabled={recalculando}
                  onClick={() => elegir(clave, valor)}
                >
                  <span aria-hidden="true">★</span>
                </button>
              ))}
            </div>
          </div>
        ))}
      </div>
      <div className={styles.acciones}>
        <button type="button" className={styles.recalcular} disabled={recalculando} onClick={recalcular}>
          {recalculando ? 'Recalculando…' : 'Recalcular'}
        </button>
        {hayPesosPropios && (
          <button
            type="button"
            className={styles.restablecer}
            disabled={recalculando}
            onClick={restablecer}
          >
            Valores por defecto
          </button>
        )}
      </div>
      {error && (
        <p role="alert" className={styles.error}>
          {error}
        </p>
      )}
    </section>
  )
}