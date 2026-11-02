import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { ApiError } from '../api/ApiError'
import {
  aprobarRequisicion,
  listarRequisiciones,
  obtenerObras,
  rechazarRequisicion,
} from '../requisiciones/requisicionesService'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import { usePendientes } from '../notificaciones/usePendientes'
import { cantidadConUnidad } from '../requisiciones/unidades'
import styles from './AprobarRequisicionesPage.module.css'

function mensajeDeErrorAprobar(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 403) return 'No tenés permiso para aprobar requisiciones.'
    if (err.status === 404) return err.detail ?? 'La requisición ya no existe. Recargá la lista.'
    if (err.status === 409) {
      return err.detail ?? 'La requisición ya no está pendiente. Recargá la lista.'
    }
    if (err.status === 400) return err.detail ?? 'No se pudo aprobar la requisición.'
  }
  return 'No se pudo conectar con el servidor. Intentá nuevamente.'
}

function mensajeDeErrorRechazar(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 403) return 'No tenés permiso para rechazar requisiciones.'
    if (err.status === 404) return err.detail ?? 'La requisición ya no existe. Recargá la lista.'
    if (err.status === 409) {
      return err.detail ?? 'La requisición ya no está pendiente. Recargá la lista.'
    }
    if (err.status === 400) return err.detail ?? 'No se pudo rechazar la requisición.'
  }
  return 'No se pudo conectar con el servidor. Intentá nuevamente.'
}

export function AprobarRequisicionesPage() {
  const [pendientes, setPendientes] = useState<RequisicionResponseDto[]>([])
  const [nombresObras, setNombresObras] = useState<Record<string, string>>({})
  const [cargando, setCargando] = useState(true)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)
  const [operacionEnVuelo, setOperacionEnVuelo] = useState<{
    tipo: 'aprobar' | 'rechazar'
    id: string
  } | null>(null)
  const [errorAccion, setErrorAccion] = useState<string | null>(null)
  const [mensajeEstado, setMensajeEstado] = useState<string | null>(null)
  const [rechazandoId, setRechazandoId] = useState<string | null>(null)
  const [motivo, setMotivo] = useState('')
  const [errorMotivo, setErrorMotivo] = useState<string | null>(null)
  const { refrescar: refrescarCampana } = usePendientes()

  // Guard inmediato contra el doble clic: "operacionEnVuelo" (estado) recién cambia en el próximo render.
  // Un único ref para proteger tanto aprobar como rechazar.
  const peticionEnVueloRef = useRef(false)

  // Cada vez que cambia "intento" se vuelve a pedir la lista (así funciona "Reintentar")
  const [intento, setIntento] = useState(0)

  useEffect(() => {
    let cancelado = false

    async function cargar() {
      try {
        const [lista, obras] = await Promise.all([
          listarRequisiciones({ estado: 'Pendiente' }),
          // Si falla la lista de obras no es grave: se muestra el identificador.
          obtenerObras().catch(() => []),
        ])
        if (cancelado) return
        setPendientes(lista)
        setNombresObras(Object.fromEntries(obras.map((o) => [o.id, o.nombre])))
      } catch {
        if (!cancelado) {
          setErrorCarga('No se pudieron cargar las requisiciones. Intentá nuevamente.')
        }
      } finally {
        if (!cancelado) setCargando(false)
      }
    }

    void cargar()
    return () => {
      cancelado = true
    }
  }, [intento])

  function reintentar() {
    setErrorCarga(null)
    setCargando(true)
    setIntento((n) => n + 1)
  }

  async function aprobar(req: RequisicionResponseDto) {
    if (peticionEnVueloRef.current) return
    peticionEnVueloRef.current = true
    setOperacionEnVuelo({ tipo: 'aprobar', id: req.requisicionId })
    setErrorAccion(null)
    setMensajeEstado(null)
    try {
      const resultado = await aprobarRequisicion(req.requisicionId)
      setPendientes((actuales) => actuales.filter((r) => r.requisicionId !== req.requisicionId))
      setMensajeEstado(`La requisición ${resultado.numeroRequisicion} fue aprobada.`)
      // La campana del encabezado tiene que bajar el número enseguida.
      refrescarCampana()
    } catch (err) {
      setErrorAccion(mensajeDeErrorAprobar(err))
    } finally {
      peticionEnVueloRef.current = false
      setOperacionEnVuelo(null)
    }
  }

  function iniciarRechazo(id: string) {
    setRechazandoId(id)
    setMotivo('')
    setErrorMotivo(null)
    setErrorAccion(null)
    setMensajeEstado(null)
  }

  function cancelarRechazo() {
    setRechazandoId(null)
    setMotivo('')
    setErrorMotivo(null)
  }

  async function confirmarRechazo(req: RequisicionResponseDto) {
    const motivoLimpio = motivo.trim()
    if (!motivoLimpio) {
      setErrorMotivo('El motivo de rechazo es obligatorio.')
      return
    }

    if (peticionEnVueloRef.current) return
    peticionEnVueloRef.current = true
    setOperacionEnVuelo({ tipo: 'rechazar', id: req.requisicionId })
    setErrorAccion(null)
    setMensajeEstado(null)

    try {
      const resultado = await rechazarRequisicion(req.requisicionId, motivoLimpio)
      setPendientes((actuales) => actuales.filter((r) => r.requisicionId !== req.requisicionId))
      setMensajeEstado(`La requisición ${resultado.numeroRequisicion} fue rechazada.`)
      refrescarCampana()
      setRechazandoId(null)
      setMotivo('')
      setErrorMotivo(null)
    } catch (err) {
      setErrorAccion(mensajeDeErrorRechazar(err))
      if (err instanceof ApiError && (err.status === 404 || err.status === 409)) {
        setRechazandoId(null)
        setMotivo('')
        setErrorMotivo(null)
        setIntento((n) => n + 1)
      }
    } finally {
      peticionEnVueloRef.current = false
      setOperacionEnVuelo(null)
    }
  }

  const hayPeticionEnVuelo = operacionEnVuelo !== null

  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to="/compras" className={styles.volver}>
        ← Volver a Gestión y Compras
      </Link>
      <h1>Aprobar requisiciones</h1>

      {mensajeEstado && (
        <p className={styles.exito} role="status">
          {mensajeEstado}
        </p>
      )}
      {errorAccion && (
        <p className={styles.errorApi} role="alert">
          {errorAccion}
        </p>
      )}

      {cargando && <p role="status">Cargando…</p>}

      {!cargando && errorCarga && (
        <div>
          <p className={styles.errorApi} role="alert">
            {errorCarga}
          </p>
          <button type="button" onClick={reintentar}>
            Reintentar
          </button>
        </div>
      )}

      {!cargando && !errorCarga && pendientes.length === 0 && (
        <p>No hay requisiciones pendientes de aprobación.</p>
      )}

      {!cargando && !errorCarga && pendientes.length > 0 && (
        <ul className={styles.lista}>
          {pendientes.map((req) => (
            <li key={req.requisicionId} className={styles.tarjeta}>
              <h2 className={styles.numero}>{req.numeroRequisicion}</h2>
              <p className={styles.dato}>
                Obra: {nombresObras[req.obraId] ?? req.obraId}
                {req.etapa ? ` · Etapa: ${req.etapa}` : ''}
              </p>
              {req.observaciones && (
                <p className={styles.dato}>Observaciones: {req.observaciones}</p>
              )}
              <ul className={styles.items}>
                {req.items.map((it) => (
                  <li key={it.articuloId}>
                    {cantidadConUnidad(it.cantidad, it.unidadMedida)} · {it.descripcion}
                  </li>
                ))}
              </ul>

              {rechazandoId === req.requisicionId ? (
                <div className={styles.contenedorRechazo}>
                  <label htmlFor={`motivo-${req.requisicionId}`} className={styles.labelMotivo}>
                    Motivo del rechazo
                  </label>
                  <textarea
                    id={`motivo-${req.requisicionId}`}
                    className={styles.textareaMotivo}
                    maxLength={500}
                    value={motivo}
                    onChange={(e) => {
                      setMotivo(e.target.value)
                      if (errorMotivo) setErrorMotivo(null)
                    }}
                    placeholder="Escribí el motivo del rechazo"
                    disabled={hayPeticionEnVuelo}
                  />
                  <div className={styles.contador}>{motivo.length}/500</div>
                  {errorMotivo && (
                    <p className={styles.errorMotivo} role="alert">
                      {errorMotivo}
                    </p>
                  )}
                  <div className={styles.accionesRechazo}>
                    <button
                      type="button"
                      className={styles.botonConfirmar}
                      disabled={hayPeticionEnVuelo}
                      onClick={() => void confirmarRechazo(req)}
                    >
                      {operacionEnVuelo?.tipo === 'rechazar' && operacionEnVuelo?.id === req.requisicionId
                        ? 'Rechazando…'
                        : 'Confirmar rechazo'}
                    </button>
                    <button
                      type="button"
                      className={styles.botonCancelar}
                      disabled={hayPeticionEnVuelo}
                      onClick={cancelarRechazo}
                    >
                      Cancelar
                    </button>
                  </div>
                </div>
              ) : (
                <div className={styles.acciones}>
                  <button
                    type="button"
                    className={styles.boton}
                    disabled={hayPeticionEnVuelo}
                    aria-label={`Aprobar ${req.numeroRequisicion}`}
                    onClick={() => void aprobar(req)}
                  >
                    {operacionEnVuelo?.tipo === 'aprobar' && operacionEnVuelo?.id === req.requisicionId
                      ? 'Aprobando…'
                      : 'Aprobar'}
                  </button>
                  <button
                    type="button"
                    className={styles.botonRechazar}
                    disabled={hayPeticionEnVuelo}
                    aria-label={`Rechazar ${req.numeroRequisicion}`}
                    onClick={() => iniciarRechazo(req.requisicionId)}
                  >
                    Rechazar
                  </button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
