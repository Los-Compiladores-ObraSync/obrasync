import { useRef, useState } from 'react'
import { ApiError } from '../api/ApiError'
import { guardarCalificacionProveedor } from './licitacionesService'
import styles from './CalificacionProveedor.module.css'

const ESTRELLAS = [1, 2, 3, 4, 5] as const

function mensajeDeError(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 403) return 'No tenés permiso para calificar proveedores.'
    if (err.status === 400) return err.detail ?? 'La calificación tiene que ser un número de 1 a 5.'
  }
  return 'No se pudo guardar la calificación. Intentá nuevamente.'
}

interface Props {
  proveedorId: string
  /** Nombre para mostrar y para los lectores de pantalla (si el servicio no lo manda, "este proveedor"). */
  nombreProveedor: string
  /**
   * Calificación vigente (1 a 5), null si todavía no tiene, o undefined si la pantalla no la conoce
   * (por ejemplo en Órdenes de compra, donde solo se carga una calificación nueva).
   */
  calificacionActual?: number | null
  /** Texto del título. Si no se pasa, "Calificación de <proveedor>". */
  titulo?: string
  /** Se llama cuando el servicio guardó la calificación, para que la pantalla vuelva a pedir los puntajes. */
  alGuardar: () => void
}

/**
 * Permite al Comprador cargar o cambiar la calificación (1 a 5) de un proveedor (#156).
 * Se usa en Órdenes de compra, cuando la orden ya fue despachada: se califica a quien ya cumplió.
 * Cambia el puntaje de TODAS las ofertas futuras de ese proveedor, por eso hay un botón "Guardar"
 * en lugar de guardar al tocar un número: evita un cambio por un toque equivocado.
 */
export function CalificacionProveedor({
  proveedorId,
  nombreProveedor,
  calificacionActual,
  titulo,
  alGuardar,
}: Props) {
  const [elegida, setElegida] = useState<number | null>(calificacionActual ?? null)
  // Lo último que el servicio confirmó: sirve para apagar "Guardar" hasta que se elija otra cosa.
  const [guardada, setGuardada] = useState<number | null>(calificacionActual ?? null)
  const [mostrarGuardada, setMostrarGuardada] = useState(false)
  const [guardando, setGuardando] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // Un ref (y no solo el estado) frena el doble toque: el estado se actualiza recién en el próximo render.
  const enVueloRef = useRef(false)

  const cambio = elegida !== null && elegida !== guardada

  function elegir(valor: number) {
    // Tocar el número ya elegido lo deja igual (a diferencia de un filtro, acá no se puede "quitar").
    setElegida(valor)
    setError(null)
    setMostrarGuardada(false)
  }

  async function guardar() {
    if (elegida === null || enVueloRef.current) return
    enVueloRef.current = true
    setGuardando(true)
    setError(null)
    try {
      await guardarCalificacionProveedor(proveedorId, elegida)
      setGuardada(elegida)
      setMostrarGuardada(true)
      alGuardar()
    } catch (err) {
      setError(mensajeDeError(err))
    } finally {
      enVueloRef.current = false
      setGuardando(false)
    }
  }

  return (
    <section
      className={styles.contenedor}
      aria-label={titulo ?? `Calificación de ${nombreProveedor}`}
    >
      <p className={styles.titulo}>
        {titulo ?? `Calificación de ${nombreProveedor}`}
        {calificacionActual === null && <span className={styles.sinCalificar}> (sin calificar)</span>}
      </p>
      <div className={styles.botones}>
        {ESTRELLAS.map((valor) => (
          <button
            key={valor}
            type="button"
            className={`${styles.boton} ${elegida !== null && valor <= elegida ? styles.llena : ''}`}
            aria-pressed={elegida === valor}
            aria-label={`${valor} de 5`}
            disabled={guardando}
            onClick={() => elegir(valor)}
          >
            <span aria-hidden="true">★</span>
          </button>
        ))}
      </div>
      <button
        type="button"
        className={styles.guardar}
        disabled={!cambio || guardando}
        onClick={() => void guardar()}
      >
        {guardando ? 'Guardando…' : 'Guardar calificación'}
      </button>
      {mostrarGuardada && (
        <p role="status" className={styles.guardada}>
          Calificación guardada.
        </p>
      )}
      {error && (
        <p role="alert" className={styles.error}>
          {error}
        </p>
      )}
    </section>
  )
}