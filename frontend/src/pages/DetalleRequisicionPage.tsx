import { useEffect, useState } from 'react'
import { Link, useLocation, useParams } from 'react-router'
import { ApiError } from '../api/ApiError'
import { useAuth } from '../auth/useAuth'
import { publicarLicitacion } from '../licitaciones/licitacionesService'
import { fechaLarga } from '../requisiciones/formatoFecha'
import { obtenerObras, obtenerRequisicion } from '../requisiciones/requisicionesService'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import { cantidadConUnidad } from '../requisiciones/unidades'
import styles from './DetalleRequisicionPage.module.css'

interface DetalleRequisicionPageProps {
  /** Ruta del historial al que vuelve (esta pantalla se usa en dos portales). */
  volverA: string
}

const CLASE_POR_ESTADO: Record<string, string> = {
  Pendiente: styles.pendiente,
  Aprobada: styles.aprobada,
  Rechazada: styles.rechazada,
}

type ErrorDetalle = 'noExiste' | 'falla'

/** Roles que pueden publicar la licitación (el backend lo exige igual: es solo comodidad de pantalla). */
const ROLES_QUE_PUBLICAN = ['Comprador', 'Admin']
const DIAS_PLAZO_POR_DEFECTO = 7

/** "YYYY-MM-DD" en hora local, a tantos días de hoy (es el formato del <input type="date">). */
function fechaLocalEnDias(dias: number): string {
  const f = new Date()
  f.setDate(f.getDate() + dias)
  const mes = String(f.getMonth() + 1).padStart(2, '0')
  const dia = String(f.getDate()).padStart(2, '0')
  return `${f.getFullYear()}-${mes}-${dia}`
}

/** El plazo vence al final del día elegido (hora local); la API lo recibe en UTC. */
function finDelDiaEnUtc(fechaLocal: string): string {
  return new Date(`${fechaLocal}T23:59:00`).toISOString()
}

type ResultadoPublicacion =
  | { tipo: 'ok'; fechaLimite: string }
  | { tipo: 'error'; mensaje: string }

function mensajeDeError(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 409) return 'Ya hay una licitación abierta para esta requisición.'
    if (err.status === 400) return err.detail ?? 'La fecha límite no es válida.'
    if (err.status === 403) return 'No tenés permiso para publicar licitaciones.'
    if (err.status === 404) return 'La requisición no existe o todavía no está aprobada.'
  }
  return 'No se pudo publicar la licitación. Intentá nuevamente.'
}

export function DetalleRequisicionPage({ volverA }: DetalleRequisicionPageProps) {
  const { id = '' } = useParams()
  // Los filtros del historial viajan en la URL (?estado=…&obraId=…): al volver se conservan.
  const { search } = useLocation()
  const { usuario } = useAuth()

  const [requisicion, setRequisicion] = useState<RequisicionResponseDto | null>(null)
  const [nombresObras, setNombresObras] = useState<Record<string, string>>({})
  const [cargando, setCargando] = useState(true)
  const [error, setError] = useState<ErrorDetalle | null>(null)

  // Cada vez que cambia "intento" se vuelve a pedir (así funciona "Reintentar")
  const [intento, setIntento] = useState(0)

  // Publicación de la licitación (solo Comprador/Admin, sobre una requisición aprobada)
  const [fechaLimite, setFechaLimite] = useState(() => fechaLocalEnDias(DIAS_PLAZO_POR_DEFECTO))
  const [publicando, setPublicando] = useState(false)
  const [resultado, setResultado] = useState<ResultadoPublicacion | null>(null)

  useEffect(() => {
    let cancelado = false

    async function cargar() {
      try {
        const [datos, obras] = await Promise.all([
          obtenerRequisicion(id),
          // Si falla la lista de obras no es grave: se muestra el identificador.
          obtenerObras().catch(() => []),
        ])
        if (cancelado) return
        setRequisicion(datos)
        setNombresObras(Object.fromEntries(obras.map((o) => [o.id, o.nombre])))
      } catch (err) {
        if (cancelado) return
        setError(err instanceof ApiError && err.status === 404 ? 'noExiste' : 'falla')
      } finally {
        if (!cancelado) setCargando(false)
      }
    }

    void cargar()
    return () => {
      cancelado = true
    }
  }, [id, intento])

  function reintentar() {
    setError(null)
    setCargando(true)
    setIntento((n) => n + 1)
  }

  // El Capataz ve solo las requisiciones que pidió él (igual que en el historial). Si abre la
  // de otra persona, se le muestra como si no existiera. Ojo: es comodidad de pantalla, NO
  // seguridad: la API devuelve la requisición a cualquier Capataz (ver la nota de #83).
  const esAjena =
    requisicion !== null &&
    usuario?.rol === 'Capataz' &&
    requisicion.solicitanteId !== usuario.id
  const noExiste = error === 'noExiste' || esAjena

  const puedePublicar =
    requisicion?.estado === 'Aprobada' &&
    usuario !== null &&
    usuario !== undefined &&
    ROLES_QUE_PUBLICAN.includes(usuario.rol)

  async function publicar() {
    if (!requisicion || !fechaLimite || publicando) return
    setPublicando(true)
    setResultado(null)
    try {
      const licitacion = await publicarLicitacion({
        requisicionId: requisicion.requisicionId,
        fechaLimiteCotizacion: finDelDiaEnUtc(fechaLimite),
      })
      setResultado({ tipo: 'ok', fechaLimite: licitacion.fechaLimiteCotizacion })
    } catch (err) {
      setResultado({ tipo: 'error', mensaje: mensajeDeError(err) })
    } finally {
      setPublicando(false)
    }
  }

  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to={{ pathname: volverA, search }} className={styles.volver}>
        ← Volver al historial
      </Link>

      {cargando && <p role="status">Cargando…</p>}

      {!cargando && noExiste && (
        <p className={styles.errorApi} role="alert">
          No encontramos esa requisición.
        </p>
      )}

      {!cargando && !noExiste && error === 'falla' && (
        <div>
          <p className={styles.errorApi} role="alert">
            No se pudo cargar la requisición. Intentá nuevamente.
          </p>
          <button type="button" onClick={reintentar}>
            Reintentar
          </button>
        </div>
      )}

      {!cargando && !noExiste && !error && requisicion && (
        <article className={styles.tarjeta}>
          <div className={styles.encabezado}>
            <h1 className={styles.numero}>{requisicion.numeroRequisicion}</h1>
            <span
              className={`${styles.estado} ${CLASE_POR_ESTADO[requisicion.estado] ?? ''}`}
            >
              {requisicion.estado}
            </span>
          </div>

          <p className={styles.dato}>
            Obra: {nombresObras[requisicion.obraId] ?? requisicion.obraId}
          </p>
          {requisicion.etapa && <p className={styles.dato}>Etapa: {requisicion.etapa}</p>}
          {requisicion.observaciones && (
            <p className={styles.dato}>Observaciones: {requisicion.observaciones}</p>
          )}
          <p className={styles.dato}>Creada el {fechaLarga(requisicion.fechaCreacion)}</p>
          {requisicion.fechaAprobacion && (
            <p className={styles.dato}>
              Aprobada el {fechaLarga(requisicion.fechaAprobacion)}
            </p>
          )}

          <h2 className={styles.subtitulo}>Materiales</h2>
          <ul className={styles.items} aria-label="Materiales pedidos">
            {requisicion.items.map((it) => (
              <li key={it.articuloId}>
                {cantidadConUnidad(it.cantidad, it.unidadMedida)} · {it.descripcion}
              </li>
            ))}
          </ul>

          {puedePublicar && (
            <section className={styles.publicar} aria-label="Publicar licitación">
              <h2 className={styles.subtitulo}>Publicar licitación</h2>
              {resultado?.tipo === 'ok' ? (
                <p className={styles.exito} role="status">
                  Licitación publicada. Los proveedores pueden cotizar hasta el{' '}
                  {fechaLarga(resultado.fechaLimite)}.
                </p>
              ) : (
                <>
                  <label className={styles.etiqueta} htmlFor="fecha-limite">
                    Fecha límite para cotizar
                  </label>
                  <input
                    id="fecha-limite"
                    type="date"
                    className={styles.campoFecha}
                    value={fechaLimite}
                    min={fechaLocalEnDias(1)}
                    onChange={(e) => setFechaLimite(e.target.value)}
                  />
                  {resultado?.tipo === 'error' && (
                    <p className={styles.errorApi} role="alert">
                      {resultado.mensaje}
                    </p>
                  )}
                  <button
                    type="button"
                    className={styles.botonPublicar}
                    onClick={() => void publicar()}
                    disabled={publicando || !fechaLimite}
                  >
                    {publicando ? 'Publicando…' : 'Publicar licitación'}
                  </button>
                </>
              )}
            </section>
          )}
        </article>
      )}
    </section>
  )
}
