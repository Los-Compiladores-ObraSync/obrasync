import { useEffect, useState } from 'react'
import { Link, useLocation, useSearchParams } from 'react-router'
import { useAuth } from '../auth/useAuth'
import { fechaLarga } from '../requisiciones/formatoFecha'
import { listarRequisiciones, obtenerObras } from '../requisiciones/requisicionesService'
import type {
  EstadoRequisicion,
  ObraResumen,
  RequisicionResponseDto,
} from '../requisiciones/requisicionesTipos'
import styles from './HistorialRequisicionesPage.module.css'

interface HistorialRequisicionesPageProps {
  /** Ruta del portal al que vuelve el enlace de arriba (esta pantalla se usa en dos portales). */
  volverA: string
  textoVolver: string
}

// Los tres valores que acepta el backend en el parámetro "estado".
const ESTADOS: readonly EstadoRequisicion[] = ['Pendiente', 'Aprobada', 'Rechazada']

const CLASE_POR_ESTADO: Record<string, string> = {
  Pendiente: styles.pendiente,
  Aprobada: styles.aprobada,
  Rechazada: styles.rechazada,
}

export function HistorialRequisicionesPage({
  volverA,
  textoVolver,
}: HistorialRequisicionesPageProps) {
  const { usuario } = useAuth()
  const [requisiciones, setRequisiciones] = useState<RequisicionResponseDto[]>([])
  const [obras, setObras] = useState<ObraResumen[]>([])

  // Los filtros viven en la URL (?estado=…&obraId=…): así se conservan al entrar al detalle
  // de una requisición y volver, y también al recargar la página.
  const [params, setParams] = useSearchParams()
  const { pathname, search } = useLocation()
  const estadoParam = params.get('estado') ?? ''
  const estadoFiltro = ESTADOS.some((e) => e === estadoParam) ? estadoParam : ''
  const obraFiltro = params.get('obraId') ?? ''
  const [cargando, setCargando] = useState(true)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)

  // Cada vez que cambia "intento" se vuelve a pedir la lista (así funciona "Reintentar")
  const [intento, setIntento] = useState(0)

  // Las obras se piden una sola vez (sirven para los nombres y para el selector de obra).
  // Si falla no es grave: se muestra el identificador y el selector queda solo con "Todas".
  useEffect(() => {
    let cancelado = false

    obtenerObras()
      .then((lista) => {
        if (!cancelado) setObras(lista)
      })
      .catch(() => {})

    return () => {
      cancelado = true
    }
  }, [intento])

  // La lista se vuelve a pedir cada vez que cambia un filtro. El filtrado lo hace el backend
  // (parámetros estado y obraId). Si una respuesta vieja llega tarde, "cancelado" la descarta.
  useEffect(() => {
    let cancelado = false

    async function cargar() {
      try {
        const lista = await listarRequisiciones({
          estado: estadoFiltro || undefined,
          obraId: obraFiltro || undefined,
        })
        if (!cancelado) setRequisiciones(lista)
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
  }, [intento, estadoFiltro, obraFiltro])

  function reintentar() {
    setErrorCarga(null)
    setCargando(true)
    setIntento((n) => n + 1)
  }

  // Cada cambio de filtro muestra "Cargando…" hasta que llega la lista nueva.
  function cambiarFiltro(nombre: 'estado' | 'obraId', valor: string) {
    const siguiente = new URLSearchParams(params)
    if (valor) siguiente.set(nombre, valor)
    else siguiente.delete(nombre)
    // replace: elegir filtros no llena de pasos el botón "atrás" del navegador.
    setParams(siguiente, { replace: true })
    setErrorCarga(null)
    setCargando(true)
  }

  function quitarFiltros() {
    setParams({}, { replace: true })
    setErrorCarga(null)
    setCargando(true)
  }

  const nombresObras = Object.fromEntries(obras.map((o) => [o.id, o.nombre]))
  const hayFiltros = estadoFiltro !== '' || obraFiltro !== ''

  // El Comprador y el Admin ven todas. El Capataz ve solo las que pidió él: la API no filtra
  // por solicitante (devuelve lo mismo a cualquier Capataz), así que se filtra acá.
  // Ojo: esto es comodidad de pantalla, NO seguridad (ver la nota de la tarjeta #83).
  const esCapataz = usuario?.rol === 'Capataz'
  const visibles = esCapataz
    ? requisiciones.filter((r) => r.solicitanteId === usuario?.id)
    : requisiciones

  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to={volverA} className={styles.volver}>
        {textoVolver}
      </Link>
      <h1>Historial de requisiciones</h1>

      <div className={styles.filtros} role="group" aria-label="Filtros del historial">
        <div className={styles.filtro}>
          <label htmlFor="filtro-estado">Estado</label>
          <select
            id="filtro-estado"
            value={estadoFiltro}
            onChange={(e) => cambiarFiltro('estado', e.target.value)}
          >
            <option value="">Todos</option>
            {ESTADOS.map((estado) => (
              <option key={estado} value={estado}>
                {estado}
              </option>
            ))}
          </select>
        </div>
        <div className={styles.filtro}>
          <label htmlFor="filtro-obra">Obra</label>
          <select
            id="filtro-obra"
            value={obraFiltro}
            onChange={(e) => cambiarFiltro('obraId', e.target.value)}
          >
            <option value="">Todas</option>
            {obras.map((obra) => (
              <option key={obra.id} value={obra.id}>
                {obra.nombre}
              </option>
            ))}
          </select>
        </div>
        <button
          type="button"
          className={styles.quitar}
          onClick={quitarFiltros}
          disabled={!hayFiltros}
        >
          Quitar filtros
        </button>
      </div>

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

      {!cargando && !errorCarga && visibles.length === 0 && (
        <p>
          {hayFiltros
            ? 'No hay requisiciones con esos filtros.'
            : esCapataz
              ? 'Todavía no hiciste ninguna requisición.'
              : 'Todavía no hay requisiciones.'}
        </p>
      )}

      {!cargando && !errorCarga && visibles.length > 0 && (
        <ul className={styles.lista}>
          {visibles.map((req) => (
            <li key={req.requisicionId} className={styles.tarjeta}>
              <div className={styles.encabezado}>
                <h2 className={styles.numero}>
                  {/* Toda la tarjeta es clickeable (ver .enlaceFila::after). Los filtros viajan en "search". */}
                  <Link
                    to={{ pathname: `${pathname.replace(/\/$/, '')}/${req.requisicionId}`, search }}
                    className={styles.enlaceFila}
                  >
                    {req.numeroRequisicion}
                  </Link>
                </h2>
                <span
                  className={`${styles.estado} ${CLASE_POR_ESTADO[req.estado] ?? ''}`}
                >
                  {req.estado}
                </span>
              </div>
              <p className={styles.dato}>
                Obra: {nombresObras[req.obraId] ?? req.obraId}
                {req.etapa ? ` · Etapa: ${req.etapa}` : ''}
              </p>
              <p className={styles.dato}>Creada el {fechaLarga(req.fechaCreacion)}</p>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
