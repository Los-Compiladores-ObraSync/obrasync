import { useEffect, useRef, useState } from 'react'
import { ApiError } from '../api/ApiError'
import { guardarCalificacionOrdenCompra, obtenerMiCalificacionOrdenCompra } from './licitacionesService'
import styles from './CalificacionProveedor.module.css'

const ESTRELLAS = [1, 2, 3, 4, 5] as const

function mensajeDeError(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 403) return 'No tenés permiso para calificar esta orden.'
    if (err.status === 409) return 'Vas a poder calificar cuando llegue todo el pedido.'
    if (err.status === 400) return err.detail ?? 'La calificación debe ser de 1 a 5 estrellas y el comentario no puede superar 300 caracteres.'
  }
  return 'No se pudo guardar la calificación. Intentá nuevamente.'
}

interface Props {
  ordenCompraId: string
  nombreProveedor: string
  titulo: string
  recibidaCompleta: boolean
  calificacionActual?: number | null
  comentarioActual?: string | null
  alGuardar: (calificacion: number, comentario: string | null) => void
}

/** Calificación por orden; la API es la fuente de verdad y el listado trae la calificación del usuario actual. */
export function CalificacionProveedor({
  ordenCompraId,
  nombreProveedor,
  titulo,
  recibidaCompleta,
  calificacionActual,
  comentarioActual,
  alGuardar,
}: Props) {
  const [guardada, setGuardada] = useState<number | null>(calificacionActual ?? null)
  const [elegida, setElegida] = useState<number | null>(calificacionActual ?? null)
  const [comentario, setComentario] = useState(comentarioActual ?? '')
  const [fija, setFija] = useState(calificacionActual != null)
  const [guardando, setGuardando] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const enVueloRef = useRef(false)
  const [cargandoRecuperada, setCargandoRecuperada] = useState(
    recibidaCompleta && calificacionActual === undefined,
  )

  useEffect(() => {
    try {
      window.localStorage.removeItem(`obrasync.calificacion.orden.${ordenCompraId}`)
    } catch {
      // La calificación ya se lee del servicio; sin almacenamiento no cambia el flujo.
    }
  }, [ordenCompraId])

  // El listado normalmente incluye estos campos. Si una respuesta compatible los omite, recuperamos
  // la calificación propia desde el GET por orden definido en el contrato.
  useEffect(() => {
    if (!recibidaCompleta || calificacionActual !== undefined) return
    let cancelado = false
    obtenerMiCalificacionOrdenCompra(ordenCompraId)
      .then((respuesta) => {
        if (cancelado) return
        setGuardada(respuesta.calificacion)
        setElegida(respuesta.calificacion)
        setComentario(respuesta.comentario ?? '')
        setFija(true)
      })
      .catch((err: unknown) => {
        if (cancelado) return
        if (err instanceof ApiError && err.status === 404) {
          setFija(false)
        } else {
          setError(mensajeDeError(err))
        }
      })
      .finally(() => {
        if (!cancelado) setCargandoRecuperada(false)
      })
    return () => {
      cancelado = true
    }
  }, [calificacionActual, ordenCompraId, recibidaCompleta])

  if (!recibidaCompleta) {
    return (
      <section className={styles.contenedor} aria-label={titulo}>
        <p className={styles.espera}>Vas a poder calificar cuando llegue todo el pedido.</p>
      </section>
    )
  }

  if (cargandoRecuperada) {
    return <section className={styles.contenedor} aria-label={titulo}><p role="status">Cargando calificación…</p></section>
  }

  async function guardar() {
    if (elegida === null || enVueloRef.current) return
    enVueloRef.current = true
    setGuardando(true)
    setError(null)
    try {
      const respuesta = await guardarCalificacionOrdenCompra(ordenCompraId, elegida, comentario)
      const texto = respuesta.comentario?.trim() || null
      setGuardada(respuesta.calificacion)
      setElegida(respuesta.calificacion)
      setComentario(texto ?? '')
      setFija(true)
      alGuardar(respuesta.calificacion, texto)
    } catch (err) {
      setError(mensajeDeError(err))
    } finally {
      enVueloRef.current = false
      setGuardando(false)
    }
  }

  return (
    <section className={styles.contenedor} aria-label={titulo}>
      <p className={styles.titulo}>{titulo || `Calificar ${nombreProveedor}`}</p>
      {fija ? (
        <>
          <p role="status" className={styles.guardada}>Calificaste con {guardada} de 5.</p>
          {comentario && <p className={styles.comentarioGuardado}>Comentario: {comentario}</p>}
          <button
            type="button"
            className={styles.guardar}
            onClick={() => setFija(false)}
          >
            Cambiar calificación
          </button>
        </>
      ) : (
        <>
          <div className={styles.botones} role="group" aria-label="Calificación de 1 a 5 estrellas">
            {ESTRELLAS.map((valor) => (
              <button
                key={valor}
                type="button"
                className={`${styles.boton} ${elegida !== null && valor <= elegida ? styles.llena : ''}`}
                aria-pressed={elegida === valor}
                aria-label={`${valor} de 5`}
                disabled={guardando}
                onClick={() => {
                  setElegida(valor)
                  setError(null)
                }}
              >
                <span aria-hidden="true">★</span>
              </button>
            ))}
          </div>
          <label className={styles.campoComentario}>
            Comentario (opcional)
            <textarea
              value={comentario}
              maxLength={300}
              rows={3}
              disabled={guardando}
              onChange={(evento) => setComentario(evento.currentTarget.value.slice(0, 300))}
              aria-label="Comentario opcional, máximo 300 caracteres"
            />
            <span>{comentario.length}/300</span>
          </label>
          <button
            type="button"
            className={styles.guardar}
            disabled={elegida === null || guardando}
            onClick={() => void guardar()}
          >
            {guardando ? 'Guardando…' : 'Guardar calificación'}
          </button>
        </>
      )}
      {error && <p role="alert" className={styles.error}>{error}</p>}
    </section>
  )
}
