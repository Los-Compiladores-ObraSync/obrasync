import { useState } from 'react'
import {
  CRITERIOS_SCORING,
  pesosParaCampos,
  validarPesos,
  type ClaveCriterio,
  type PesosEscritos,
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
 * Los campos son porcentajes; no hace falta que sumen 100: el servicio los normaliza.
 * Ejemplo: 50 / 20 / 20 / 10 y 5 / 2 / 2 / 1 dan el mismo resultado.
 */
export function PonderacionesScoring({
  ponderacionesActuales,
  hayPesosPropios,
  recalculando,
  errorServicio,
  alRecalcular,
  alRestablecer,
}: Props) {
  const [campos, setCampos] = useState<PesosEscritos>(() => pesosParaCampos(ponderacionesActuales))
  const [errorLocal, setErrorLocal] = useState<string | null>(null)

  function cambiar(clave: ClaveCriterio, valor: string) {
    setCampos((anteriores) => ({ ...anteriores, [clave]: valor }))
    setErrorLocal(null)
  }

  function recalcular() {
    const resultado = validarPesos(campos)
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
    <section className={styles.contenedor} aria-label="Importancia de cada criterio">
      <h3 className={styles.titulo}>Qué tan importante es cada criterio</h3>
      <p className={styles.ayuda}>
        El puntaje suma lo que cada oferta obtiene en precio, flete, plazo y calificación del
        proveedor. Acá elegís cuánto pesa cada uno (no hace falta que sumen 100).
      </p>
      <div className={styles.campos}>
        {CRITERIOS_SCORING.map(({ clave, etiqueta }) => (
          <label key={clave} className={styles.campo}>
            <span>{etiqueta} (%)</span>
            <input
              type="text"
              inputMode="decimal"
              value={campos[clave]}
              disabled={recalculando}
              onChange={(e) => cambiar(clave, e.target.value)}
            />
          </label>
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