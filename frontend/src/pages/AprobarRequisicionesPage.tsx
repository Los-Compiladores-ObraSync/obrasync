import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router'
import { ApiError } from '../api/ApiError'
import {
  aprobarRequisicion,
  listarRequisiciones,
  obtenerObras,
} from '../requisiciones/requisicionesService'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
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

export function AprobarRequisicionesPage() {
  const [pendientes, setPendientes] = useState<RequisicionResponseDto[]>([])
  const [nombresObras, setNombresObras] = useState<Record<string, string>>({})
  const [cargando, setCargando] = useState(true)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)
  const [aprobandoId, setAprobandoId] = useState<string | null>(null)
  const [errorAprobar, setErrorAprobar] = useState<string | null>(null)
  const [aprobada, setAprobada] = useState<string | null>(null)

  const cargar = useCallback(async () => {
    setCargando(true)
    setErrorCarga(null)
    try {
      const [lista, obras] = await Promise.all([
        listarRequisiciones({ estado: 'Pendiente' }),
        // Si falla la lista de obras no es grave: se muestra el identificador.
        obtenerObras().catch(() => []),
      ])
      setPendientes(lista)
      setNombresObras(Object.fromEntries(obras.map((o) => [o.id, o.nombre])))
    } catch {
      setErrorCarga('No se pudieron cargar las requisiciones. Intentá nuevamente.')
    } finally {
      setCargando(false)
    }
  }, [])

  useEffect(() => {
    void cargar()
  }, [cargar])

  async function aprobar(req: RequisicionResponseDto) {
    setAprobandoId(req.requisicionId)
    setErrorAprobar(null)
    setAprobada(null)
    try {
      const resultado = await aprobarRequisicion(req.requisicionId)
      setPendientes((actuales) => actuales.filter((r) => r.requisicionId !== req.requisicionId))
      setAprobada(resultado.numeroRequisicion)
    } catch (err) {
      setErrorAprobar(mensajeDeErrorAprobar(err))
    } finally {
      setAprobandoId(null)
    }
  }

  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to="/compras" className={styles.volver}>
        ← Volver a Gestión y Compras
      </Link>
      <h1>Aprobar requisiciones</h1>

      {aprobada && (
        <p className={styles.exito} role="status">
          La requisición {aprobada} fue aprobada.
        </p>
      )}
      {errorAprobar && (
        <p className={styles.errorApi} role="alert">
          {errorAprobar}
        </p>
      )}

      {cargando && <p role="status">Cargando…</p>}

      {!cargando && errorCarga && (
        <div>
          <p className={styles.errorApi} role="alert">
            {errorCarga}
          </p>
          <button type="button" onClick={() => void cargar()}>
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
              {req.observaciones && <p className={styles.dato}>Observaciones: {req.observaciones}</p>}
              <ul className={styles.items}>
                {req.items.map((it) => (
                  <li key={it.articuloId}>
                    {cantidadConUnidad(it.cantidad, it.unidadMedida)} · {it.descripcion}
                  </li>
                ))}
              </ul>
              <button
                type="button"
                className={styles.boton}
                disabled={aprobandoId !== null}
                aria-label={`Aprobar ${req.numeroRequisicion}`}
                onClick={() => void aprobar(req)}
              >
                {aprobandoId === req.requisicionId ? 'Aprobando…' : 'Aprobar'}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
