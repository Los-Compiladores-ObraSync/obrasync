import { useContext, useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { ApiError } from '../api/ApiError'
import { AuthContext } from '../auth/sessionContext'
import { useBloqueadas } from '../notificaciones/useBloqueadas'
import { formatearPesos } from '../presupuesto/formatoPesos'
import { FormularioNuevaPartida } from '../presupuesto/FormularioNuevaPartida'
import {
  liberarOrdenCompra,
  obtenerPartidas,
} from '../presupuesto/presupuestoService'
import type {
  OrdenCompraBloqueada,
  PartidaPresupuestaria,
} from '../presupuesto/presupuestoTipos'
import { obtenerObras } from '../requisiciones/requisicionesService'
import type { ObraResumen } from '../requisiciones/requisicionesTipos'
import styles from './PresupuestoPage.module.css'

interface EstadoError {
  mensaje: string
  es403: boolean
}

function mensajeDeErrorLiberar(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 403) return 'Solo un Administrador puede autorizar'
    if (err.status === 404) return 'La orden ya no existe'
    if (err.status === 409) return 'La orden ya no está bloqueada'
    if (err.status === 400) return err.detail ?? 'Datos de solicitud inválidos.'
  }
  return 'No se pudo conectar con el servidor. Intentá nuevamente.'
}

export function PresupuestoPage() {
  const auth = useContext(AuthContext)
  const {
    bloqueadas: ordenesBloqueadas,
    refrescar: refrescarOrdenesBloqueadas,
    reconciliarOrdenNoBloqueada,
    cargado: bloqueadasCargadas,
    huboError: errorBloqueadas,
  } = useBloqueadas()
  const esAdmin = auth?.usuario?.rol === 'Admin'
  // Cargar partidas: Comprador y Admin (el backend también lo exige, #161)
  const puedeCargarPartidas = esAdmin || auth?.usuario?.rol === 'Comprador'

  const [searchParams, setSearchParams] = useSearchParams()
  const obraParam = searchParams.get('obraId') ?? ''

  const [obras, setObras] = useState<ObraResumen[]>([])
  const [obraSeleccionadaId, setObraSeleccionadaId] = useState<string>(obraParam)
  const [partidas, setPartidas] = useState<PartidaPresupuestaria[]>([])
  const [cargando, setCargando] = useState(Boolean(obraParam))
  const [error, setError] = useState<EstadoError | null>(null)
  const [intento, setIntento] = useState(0)

  // Las órdenes bloqueadas vienen del proveedor compartido con la campana (#97).
  const [autorizandoId, setAutorizandoId] = useState<string | null>(null)
  const [motivoLiberacion, setMotivoLiberacion] = useState('')
  const [liberandoId, setLiberandoId] = useState<string | null>(null)
  const [errorAccion, setErrorAccion] = useState<string | null>(null)
  const [mensajeExito, setMensajeExito] = useState<string | null>(null)

  // Protección inmediata contra el doble clic
  const peticionEnVueloRef = useRef(false)

  // Carga inicial de obras y órdenes bloqueadas
  useEffect(() => {
    let cancelado = false

    async function cargarInicial() {
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

    void cargarInicial()
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

  function iniciarAutorizacion(id: string) {
    setAutorizandoId(id)
    setMotivoLiberacion('')
    setErrorAccion(null)
    setMensajeExito(null)
  }

  function cancelarAutorizacion() {
    setAutorizandoId(null)
    setMotivoLiberacion('')
  }

  async function confirmarLiberacion(orden: OrdenCompraBloqueada) {
    if (peticionEnVueloRef.current) return
    peticionEnVueloRef.current = true
    setLiberandoId(orden.ordenCompraId)
    setErrorAccion(null)
    setMensajeExito(null)

    try {
      await liberarOrdenCompra(orden.ordenCompraId, {
        motivo: motivoLiberacion.trim() || null,
      })
      setMensajeExito('Orden liberada')
      setAutorizandoId(null)
      setMotivoLiberacion('')
      refrescarOrdenesBloqueadas()
      // Vuelve a pedir el tablero para reflejar los montos actualizados
      setIntento((n) => n + 1)
    } catch (err) {
      const msg = mensajeDeErrorLiberar(err)
      setErrorAccion(msg)
      if (err instanceof ApiError && err.status === 409) {
        // El backend confirma que ya cambió de estado: retirarla inmediatamente y reconciliar.
        reconciliarOrdenNoBloqueada(orden.ordenCompraId)
        setAutorizandoId(null)
        setMotivoLiberacion('')
      }
    } finally {
      peticionEnVueloRef.current = false
      setLiberandoId(null)
    }
  }

  function handlePartidaCreada(partida: PartidaPresupuestaria) {
    setPartidas((previas) => [...previas, partida])
    setMensajeExito(`Partida ${partida.codigoPartida} cargada correctamente.`)
  }

  const hayPeticionEnVuelo = liberandoId !== null
  const nombresObras = Object.fromEntries(obras.map((o) => [o.id, o.nombre]))

  return (
    <section className={styles.contenedor}>
      <Link to="/" className={styles.volver}>
        ← Volver al inicio
      </Link>
      <h1 className={styles.titulo}>Tablero de desvíos presupuestarios</h1>
      <p className={styles.intro}>
        Control y seguimiento de partidas presupuestarias asignadas por obra y etapa.
      </p>

      {mensajeExito && (
        <p className={styles.exito} role="status">
          {mensajeExito}
        </p>
      )}

      {errorAccion && (
        <p className={styles.errorApi} role="alert">
          {errorAccion}
        </p>
      )}

      {errorBloqueadas && (
        <div role="alert" className={styles.errorApi}>
          <p>
            {bloqueadasCargadas
              ? 'No se pudieron actualizar las órdenes bloqueadas. Se conservan los datos anteriores.'
              : 'No se pudieron cargar las órdenes bloqueadas. No es posible confirmar si hay órdenes.'}
          </p>
          <button type="button" onClick={refrescarOrdenesBloqueadas} className={styles.botonReintentar}>
            Reintentar
          </button>
        </div>
      )}

      {!errorBloqueadas && !bloqueadasCargadas && (
        <p role="status" className={styles.estadoMensaje}>Cargando órdenes bloqueadas…</p>
      )}

      {/* Sección de Órdenes Bloqueadas: SOLO si hay órdenes */}
      {ordenesBloqueadas.length > 0 && (
        <section
          className={styles.seccionBloqueadas}
          aria-label="Órdenes de compra bloqueadas"
        >
          <h2 className={styles.tituloBloqueadas}>Órdenes de compra bloqueadas</h2>
          <ul className={styles.listaBloqueadas}>
            {ordenesBloqueadas.map((orden) => (
              <li key={orden.ordenCompraId} className={styles.tarjetaBloqueada}>
                <h3 className={styles.tituloOrdenBloqueada}>
                  Orden {orden.ordenCompraId.slice(0, 8).toUpperCase()}
                </h3>
                <p className={styles.textoExceso}>
                  Supera el presupuesto por {formatearPesos(orden.exceso)}
                </p>
                <p className={styles.datoBloqueada}>
                  <strong>Obra:</strong> {nombresObras[orden.obraId] ?? orden.obraId} ·{' '}
                  <strong>Etapa:</strong> {orden.etapa ?? 'General de la obra'}
                </p>

                <p className={styles.motivoBloqueo}>
                  <strong>Motivo:</strong> {orden.motivo}
                </p>

                <div className={styles.grillaBloqueada}>
                  <div className={styles.montoFila}>
                    <span className={styles.montoLabel}>Monto de la orden:</span>
                    <span className={styles.montoValor}>
                      {formatearPesos(orden.montoOrdenCompra)}
                    </span>
                  </div>
                  <div className={styles.montoFila}>
                    <span className={styles.montoLabel}>Presupuesto asignado:</span>
                    <span className={styles.montoValor}>
                      {formatearPesos(orden.montoAsignadoPartida)}
                    </span>
                  </div>
                  <div className={styles.montoFila}>
                    <span className={styles.montoLabel}>Ya comprometido:</span>
                    <span className={styles.montoValor}>
                      {formatearPesos(orden.montoComprometidoActual)}
                    </span>
                  </div>
                </div>

                {esAdmin ? (
                  autorizandoId === orden.ordenCompraId ? (
                    <div className={styles.contenedorConfirmacion}>
                      <label
                        htmlFor={`motivo-${orden.ordenCompraId}`}
                        className={styles.labelMotivo}
                      >
                        Motivo de la autorización (opcional)
                      </label>
                      <textarea
                        id={`motivo-${orden.ordenCompraId}`}
                        className={styles.textareaMotivo}
                        maxLength={500}
                        value={motivoLiberacion}
                        onChange={(e) => setMotivoLiberacion(e.target.value)}
                        placeholder="Escribí el motivo de la autorización"
                        disabled={hayPeticionEnVuelo}
                      />
                      <div className={styles.contador}>
                        {motivoLiberacion.length}/500
                      </div>
                      <div className={styles.accionesConfirmacion}>
                        <button
                          type="button"
                          className={styles.botonConfirmarLiberacion}
                          disabled={hayPeticionEnVuelo}
                          onClick={() => void confirmarLiberacion(orden)}
                        >
                          {liberandoId === orden.ordenCompraId
                            ? 'Liberando…'
                            : 'Confirmar autorización'}
                        </button>
                        <button
                          type="button"
                          className={styles.botonCancelar}
                          disabled={hayPeticionEnVuelo}
                          onClick={cancelarAutorizacion}
                        >
                          Cancelar
                        </button>
                      </div>
                    </div>
                  ) : (
                    <button
                      type="button"
                      className={styles.botonLiberar}
                      disabled={hayPeticionEnVuelo}
                      onClick={() => iniciarAutorizacion(orden.ordenCompraId)}
                    >
                      Autorizar y liberar
                    </button>
                  )
                ) : (
                  <p className={styles.avisoComprador}>La autoriza un Administrador</p>
                )}
              </li>
            ))}
          </ul>
        </section>
      )}

      {/* Tablero de desvíos y selector */}
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

      {puedeCargarPartidas && obraSeleccionadaId && (
        <FormularioNuevaPartida
          key={obraSeleccionadaId}
          obraId={obraSeleccionadaId}
          onCreada={handlePartidaCreada}
        />
      )}

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
