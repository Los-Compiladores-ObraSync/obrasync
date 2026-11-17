import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { ApiError } from '../api/ApiError'
import { formatearPesos } from '../presupuesto/formatoPesos'
import { obtenerPartidas } from '../presupuesto/presupuestoService'
import type { PartidaPresupuestaria } from '../presupuesto/presupuestoTipos'
import { obtenerObras } from '../requisiciones/requisicionesService'
import type { ObraResumen } from '../requisiciones/requisicionesTipos'
import styles from './PresupuestoPage.module.css'

interface EstadoError {
  mensaje: string
  es403: boolean
}

export function PresupuestoPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const obraParam = searchParams.get('obraId') ?? ''

  const [obras, setObras] = useState<ObraResumen[]>([])
  const [obraSeleccionadaId, setObraSeleccionadaId] = useState<string>(obraParam)
  const [partidas, setPartidas] = useState<PartidaPresupuestaria[]>([])
  const [cargando, setCargando] = useState(Boolean(obraParam))
  const [error, setError] = useState<EstadoError | null>(null)
  const [intento, setIntento] = useState(0)

  // Carga inicial de obras
  useEffect(() => {
    let cancelado = false

    async function cargarObras() {
      try {
        const listaObras = await obtenerObras()
        if (!cancelado) {
          setObras(listaObras)
        }
      } catch (err) {
        if (!cancelado) {
          if (err instanceof ApiError && err.status === 403) {
            setError({
              mensaje: 'No tenés permiso para ver las obras.',
              es403: true,
            })
          } else {
            setError({
              mensaje: 'No se pudieron cargar las obras. Intentá nuevamente.',
              es403: false,
            })
          }
        }
      }
    }

    void cargarObras()
    return () => {
      cancelado = true
    }
  }, [intento])

  // Carga de partidas cuando hay una obra seleccionada
  useEffect(() => {
    if (!obraSeleccionadaId) return

    let cancelado = false

    async function cargarPartidas() {
      try {
        const lista = await obtenerPartidas({ obraId: obraSeleccionadaId })
        if (!cancelado) {
          setPartidas(lista)
          setCargando(false)
        }
      } catch (err) {
        if (!cancelado) {
          if (err instanceof ApiError && err.status === 403) {
            setError({
              mensaje: 'No tenés permiso para ver las partidas presupuestarias de esta obra.',
              es403: true,
            })
          } else {
            setError({
              mensaje: 'No se pudieron cargar las partidas presupuestarias. Intentá nuevamente.',
              es403: false,
            })
          }
          setCargando(false)
        }
      }
    }

    void cargarPartidas()
    return () => {
      cancelado = true
    }
  }, [obraSeleccionadaId, intento])

  function handleCambioObra(nuevaObraId: string) {
    setObraSeleccionadaId(nuevaObraId)
    if (!nuevaObraId) {
      setPartidas([])
      setError(null)
      setCargando(false)
      setSearchParams({})
    } else {
      setCargando(true)
      setError(null)
      setSearchParams({ obraId: nuevaObraId })
    }
  }

  function reintentar() {
    setError(null)
    setCargando(true)
    setIntento((n) => n + 1)
  }

  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to="/compras" className={styles.volver}>
        ← Volver a compras
      </Link>
      <h1 className={styles.titulo}>Tablero de desvíos presupuestarios</h1>
      <p className={styles.intro}>
        Control y seguimiento de partidas presupuestarias asignadas por obra y etapa.
      </p>

      <div className={styles.selectorGrupo}>
        <label htmlFor="selector-obra" className={styles.selectorLabel}>
          Obra
        </label>
        <select
          id="selector-obra"
          className={styles.selectObra}
          value={obraSeleccionadaId}
          onChange={(e) => handleCambioObra(e.target.value)}
        >
          <option value="">Seleccioná una obra</option>
          {obras.map((o) => (
            <option key={o.id} value={o.id}>
              {o.nombre}
            </option>
          ))}
        </select>
      </div>

      {cargando && (
        <p role="status" className={styles.estadoMensaje}>
          Cargando…
        </p>
      )}

      {!cargando && error && (
        <div role="alert">
          <p className={styles.errorApi}>{error.mensaje}</p>
          <button type="button" onClick={reintentar} className={styles.botonReintentar}>
            Reintentar
          </button>
        </div>
      )}

      {!cargando && !error && obraSeleccionadaId && partidas.length === 0 && (
        <p role="status" className={styles.estadoMensaje}>
          Esta obra no tiene partidas
        </p>
      )}

      {!cargando && !error && !obraSeleccionadaId && (
        <p className={styles.estadoMensaje}>
          Seleccioná una obra para consultar sus partidas presupuestarias.
        </p>
      )}

      {!cargando && !error && partidas.length > 0 && (
        <ul className={styles.lista}>
          {partidas.map((partida) => {
            const estaExcedida = partida.montoDisponible < 0
            return (
              <li
                key={partida.id}
                className={
                  estaExcedida
                    ? `${styles.tarjeta} ${styles.tarjetaExcedida}`
                    : styles.tarjeta
                }
              >
                <div className={styles.cabeceraTarjeta}>
                  <h2 className={styles.etapaTitulo}>
                    {partida.etapa ?? 'General de la obra'}
                  </h2>
                  {estaExcedida && (
                    <span className={styles.etiquetaExcedida}>Excedida</span>
                  )}
                </div>

                <p className={styles.codigo}>Código: {partida.codigoPartida}</p>
                {partida.descripcion && (
                  <p className={styles.descripcion}>{partida.descripcion}</p>
                )}

                <div className={styles.grillaMontos}>
                  <div className={styles.montoFila}>
                    <span className={styles.montoLabel}>Asignado:</span>
                    <span className={styles.montoValor}>
                      {formatearPesos(partida.montoAsignado)}
                    </span>
                  </div>
                  <div className={styles.montoFila}>
                    <span className={styles.montoLabel}>Comprometido:</span>
                    <span className={styles.montoValor}>
                      {formatearPesos(partida.montoComprometido)}
                    </span>
                  </div>
                  <div className={styles.montoFila}>
                    <span className={styles.montoLabel}>Ejecutado:</span>
                    <span className={styles.montoValor}>
                      {formatearPesos(partida.montoEjecutado)}
                    </span>
                  </div>
                  <div className={styles.diferenciaFila}>
                    <span>Diferencia:</span>
                    <span
                      className={
                        estaExcedida ? styles.valorExcedido : styles.montoValor
                      }
                    >
                      {formatearPesos(partida.montoDisponible)}
                    </span>
                  </div>
                </div>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
