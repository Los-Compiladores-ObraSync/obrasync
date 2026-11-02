import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { useAuth } from '../auth/useAuth'
import { fechaLarga } from '../requisiciones/formatoFecha'
import { listarRequisiciones, obtenerObras } from '../requisiciones/requisicionesService'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import styles from './HistorialRequisicionesPage.module.css'

interface HistorialRequisicionesPageProps {
  /** Ruta del portal al que vuelve el enlace de arriba (esta pantalla se usa en dos portales). */
  volverA: string
  textoVolver: string
}

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
  const [nombresObras, setNombresObras] = useState<Record<string, string>>({})
  const [cargando, setCargando] = useState(true)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)

  // Cada vez que cambia "intento" se vuelve a pedir la lista (así funciona "Reintentar")
  const [intento, setIntento] = useState(0)

  useEffect(() => {
    let cancelado = false

    async function cargar() {
      try {
        const [lista, obras] = await Promise.all([
          listarRequisiciones(),
          // Si falla la lista de obras no es grave: se muestra el identificador.
          obtenerObras().catch(() => []),
        ])
        if (cancelado) return
        setRequisiciones(lista)
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
          {esCapataz
            ? 'Todavía no hiciste ninguna requisición.'
            : 'Todavía no hay requisiciones.'}
        </p>
      )}

      {!cargando && !errorCarga && visibles.length > 0 && (
        <ul className={styles.lista}>
          {visibles.map((req) => (
            <li key={req.requisicionId} className={styles.tarjeta}>
              <div className={styles.encabezado}>
                <h2 className={styles.numero}>{req.numeroRequisicion}</h2>
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
