import { useRef, useState, type ReactNode } from 'react'
import styles from './ConfirmacionAccion.module.css'
import { mensajeDeErrorAlConfirmar } from './mensajesDeError'

interface ConfirmacionAccionProps {
  /** Qué se va a hacer ("Nueva requisición"). */
  titulo: string
  /** Resumen legible para revisar antes de confirmar. Los números salen de la respuesta de la herramienta, nunca del modelo. */
  children: ReactNode
  /** Acción irreversible (por ejemplo adjudicar): muestra los datos clave más grandes y un aviso. */
  reforzada?: boolean
  textoConfirmar?: string
  textoCancelar?: string
  /** Lo que se ejecuta al confirmar: llama al endpoint existente con el JWT del usuario. Si lanza un error, se muestra acá. */
  onConfirmar: () => Promise<void>
  onCancelar: () => void
}

/** Tarjeta única de confirmación para TODA escritura del asistente (tarjeta #146).
 *  Regla de oro: el asistente nunca escribe; solo el botón Confirmar llama al endpoint.
 *  El botón queda bloqueado mientras se envía, así un doble toque no manda la acción dos veces. */
export function ConfirmacionAccion({
  titulo,
  children,
  reforzada = false,
  textoConfirmar = 'Confirmar',
  textoCancelar = 'Cancelar',
  onConfirmar,
  onCancelar,
}: ConfirmacionAccionProps) {
  const [enviando, setEnviando] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // El estado de React se actualiza en el siguiente render: esta marca evita que dos toques seguidos
  // lleguen a ejecutar dos veces la acción.
  const enCurso = useRef(false)

  async function confirmar() {
    if (enCurso.current) {
      return
    }
    enCurso.current = true
    setEnviando(true)
    setError(null)
    try {
      await onConfirmar()
    } catch (err) {
      setError(mensajeDeErrorAlConfirmar(err))
    } finally {
      enCurso.current = false
      setEnviando(false)
    }
  }

  return (
    <section
      className={`${styles.tarjeta} ${reforzada ? styles.reforzada : ''}`}
      aria-label={titulo}
    >
      <h3 className={styles.titulo}>{titulo}</h3>
      {reforzada && (
        <p className={styles.aviso}>Esta acción no se puede deshacer. Revisá bien los datos.</p>
      )}
      <div className={`${styles.resumen} ${reforzada ? styles.resumenGrande : ''}`}>{children}</div>
      {error && (
        <p role="alert" className={styles.error}>
          {error}
        </p>
      )}
      <div className={styles.botones}>
        <button type="button" className={styles.confirmar} onClick={confirmar} disabled={enviando}>
          {enviando ? 'Enviando…' : textoConfirmar}
        </button>
        <button type="button" className={styles.cancelar} onClick={onCancelar} disabled={enviando}>
          {textoCancelar}
        </button>
      </div>
    </section>
  )
}
