import { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { ApiError } from '../api/ApiError'
import {
  calcularPrecioTotalCotizacion,
  formatoPrecio,
} from '../licitaciones/cotizacionesCalculos'
import {
  adjudicarLicitacion,
  listarCotizaciones,
  listarLicitaciones,
} from '../licitaciones/licitacionesService'
import type {
  CotizacionResponseDto,
  LicitacionResponseDto,
} from '../licitaciones/licitacionesTipos'
import { fechaLarga } from '../requisiciones/formatoFecha'
import styles from './LicitacionesComprasPage.module.css'

function mensajeDeErrorAdjudicar(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 403) return 'No tenés permiso para adjudicar licitaciones.'
    if (err.status === 404) {
      return err.detail ?? 'La licitación o la cotización ya no existe. Recargá la lista.'
    }
    if (err.status === 409) {
      return err.detail ?? 'La licitación ya se encuentra adjudicada o cerrada.'
    }
    if (err.status === 400) {
      return err.detail ?? 'No se pudo adjudicar la licitación.'
    }
  }
  return 'No se pudo conectar con el servidor. Intentá nuevamente.'
}

export function LicitacionesComprasPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const licitacionIdParam = searchParams.get('licitacionId')

  const [licitaciones, setLicitaciones] = useState<LicitacionResponseDto[]>([])
  const [cargandoLicitaciones, setCargandoLicitaciones] = useState(true)
  const [errorLicitaciones, setErrorLicitaciones] = useState<string | null>(null)
  const [intentoLicitaciones, setIntentoLicitaciones] = useState(0)

  const [cotizaciones, setCotizaciones] = useState<CotizacionResponseDto[]>([])
  const [cargandoCotizaciones, setCargandoCotizaciones] = useState(false)
  const [errorCotizaciones, setErrorCotizaciones] = useState<string | null>(null)
  const [intentoCotizaciones, setIntentoCotizaciones] = useState(0)

  const [confirmandoCotizacionId, setConfirmandoCotizacionId] = useState<string | null>(null)
  const [adjudicandoCotizacionId, setAdjudicandoCotizacionId] = useState<string | null>(null)
  const [mensajeExito, setMensajeExito] = useState<string | null>(null)
  const [errorAccion, setErrorAccion] = useState<string | null>(null)

  const peticionEnVueloRef = useRef(false)

  // Cargar lista de licitaciones
  useEffect(() => {
    let cancelado = false

    async function cargar() {
      try {
        const datos = await listarLicitaciones()
        if (cancelado) return
        setLicitaciones(datos)
      } catch {
        if (!cancelado) {
          setErrorLicitaciones('No se pudieron cargar las licitaciones. Intentá nuevamente.')
        }
      } finally {
        if (!cancelado) {
          setCargandoLicitaciones(false)
        }
      }
    }

    void cargar()
    return () => {
      cancelado = true
    }
  }, [intentoLicitaciones])

  // Licitación seleccionada actual
  const licitacionSeleccionada =
    licitaciones.find((l) => l.licitacionId === licitacionIdParam) ?? null

  // Cargar cotizaciones cuando hay una licitación seleccionada
  useEffect(() => {
    let cancelado = false

    async function cargar() {
      if (!licitacionIdParam) {
        if (!cancelado) {
          setCotizaciones([])
          setCargandoCotizaciones(false)
          setErrorCotizaciones(null)
        }
        return
      }

      if (!cancelado) {
        setCargandoCotizaciones(true)
        setErrorCotizaciones(null)
        setConfirmandoCotizacionId(null)
      }

      try {
        const datos = await listarCotizaciones(licitacionIdParam)
        if (cancelado) return
        setCotizaciones(datos)
      } catch {
        if (!cancelado) {
          setErrorCotizaciones('No se pudieron cargar las cotizaciones. Intentá nuevamente.')
        }
      } finally {
        if (!cancelado) {
          setCargandoCotizaciones(false)
        }
      }
    }

    void cargar()
    return () => {
      cancelado = true
    }
  }, [licitacionIdParam, intentoCotizaciones])

  function reintentarLicitaciones() {
    setErrorLicitaciones(null)
    setCargandoLicitaciones(true)
    setIntentoLicitaciones((n) => n + 1)
  }

  function reintentarCotizaciones() {
    setErrorCotizaciones(null)
    setCargandoCotizaciones(true)
    setIntentoCotizaciones((n) => n + 1)
  }

  function seleccionarLicitacion(id: string) {
    setMensajeExito(null)
    setErrorAccion(null)
    setSearchParams({ licitacionId: id })
  }

  async function handleConfirmarAdjudicacion(cotizacion: CotizacionResponseDto) {
    if (!licitacionSeleccionada) return
    if (peticionEnVueloRef.current) return
    peticionEnVueloRef.current = true
    setAdjudicandoCotizacionId(cotizacion.cotizacionId)
    setErrorAccion(null)
    setMensajeExito(null)

    try {
      const ordenCompra = await adjudicarLicitacion(
        licitacionSeleccionada.licitacionId,
        cotizacion.cotizacionId,
      )
      setMensajeExito(
        `¡Licitación adjudicada con éxito! Se emitió la Orden de Compra ${ordenCompra.id}.`,
      )
      setConfirmandoCotizacionId(null)
      // Actualizamos el estado local de la licitación a Adjudicada
      setLicitaciones((actuales) =>
        actuales.map((l) =>
          l.licitacionId === licitacionSeleccionada.licitacionId
            ? { ...l, estado: 'Adjudicada' }
            : l,
        ),
      )
    } catch (err) {
      setErrorAccion(mensajeDeErrorAdjudicar(err))
    } finally {
      peticionEnVueloRef.current = false
      setAdjudicandoCotizacionId(null)
    }
  }

  const esLicitacionAdjudicada = licitacionSeleccionada?.estado === 'Adjudicada'

  return (
    <section className={styles.contenedor}>
      <Link to="/compras" className={styles.volver}>
        ← Volver a compras
      </Link>
      <h1>Licitaciones y cotizaciones</h1>
      <p className="intro">
        Consultá las licitaciones abiertas, compará cotizaciones recibidas y adjudicá las compras.
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

      {cargandoLicitaciones && <p role="status">Cargando licitaciones…</p>}

      {!cargandoLicitaciones && errorLicitaciones && (
        <div className={styles.seccionError}>
          <p className={styles.errorApi} role="alert">
            {errorLicitaciones}
          </p>
          <button
            type="button"
            className={styles.botonReintentar}
            onClick={reintentarLicitaciones}
          >
            Reintentar
          </button>
        </div>
      )}

      {!cargandoLicitaciones && !errorLicitaciones && licitaciones.length === 0 && (
        <p className={styles.mensajeVacio}>No hay licitaciones disponibles.</p>
      )}

      {!cargandoLicitaciones && !errorLicitaciones && licitaciones.length > 0 && (
        <>
          <h2 className={styles.subtitulo}>Lista de licitaciones</h2>
          <ul className={styles.listaLicitaciones}>
            {licitaciones.map((lic) => {
              const cantItems = lic.items?.length ?? 0
              const textoItems = `${cantItems} ${cantItems === 1 ? 'ítem' : 'ítems'}`
              const fechaLimiteTexto = fechaLarga(lic.fechaLimiteCotizacion)
              const estaSeleccionada = lic.licitacionId === licitacionIdParam

              return (
                <li
                  key={lic.licitacionId}
                  className={`${styles.tarjetaLicitacion} ${
                    estaSeleccionada ? styles.licitacionSeleccionada : ''
                  }`}
                >
                  <h3 className={styles.numeroRequisicion}>
                    {lic.numeroRequisicion ?? 'Licitación'}
                  </h3>
                  <span
                    className={`${styles.estadoBadge} ${
                      lic.estado === 'Adjudicada' ? styles.estadoAdjudicada : ''
                    }`}
                  >
                    {lic.estado}
                  </span>
                  <p className={styles.dato}>{textoItems}</p>
                  <p className={styles.dato}>
                    Fecha límite: {fechaLimiteTexto || 'Sin fecha límite'}
                  </p>
                  <button
                    type="button"
                    className={styles.botonSeleccionar}
                    onClick={() => seleccionarLicitacion(lic.licitacionId)}
                    aria-pressed={estaSeleccionada}
                  >
                    {estaSeleccionada ? 'Licitación seleccionada' : 'Ver cotizaciones'}
                  </button>
                </li>
              )
            })}
          </ul>
        </>
      )}

      {licitacionSeleccionada && (
        <div className={styles.seccionCotizaciones}>
          <div className={styles.encabezadoMatriz}>
            <h2>
              Cotizaciones para {licitacionSeleccionada.numeroRequisicion ?? 'la licitación'}
            </h2>
            {esLicitacionAdjudicada && (
              <span className={`${styles.estadoBadge} ${styles.estadoAdjudicada}`}>
                Licitación ya adjudicada
              </span>
            )}
          </div>

          {cargandoCotizaciones && <p role="status">Cargando cotizaciones…</p>}

          {!cargandoCotizaciones && errorCotizaciones && (
            <div className={styles.seccionError}>
              <p className={styles.errorApi} role="alert">
                {errorCotizaciones}
              </p>
              <button
                type="button"
                className={styles.botonReintentar}
                onClick={reintentarCotizaciones}
              >
                Reintentar
              </button>
            </div>
          )}

          {!cargandoCotizaciones && !errorCotizaciones && cotizaciones.length === 0 && (
            <p className={styles.mensajeVacio}>Esta licitación no tiene cotizaciones cargadas.</p>
          )}

          {!cargandoCotizaciones && !errorCotizaciones && cotizaciones.length > 0 && (
            <div className={styles.matrizContenedor}>
              <table
                className={styles.tabla}
                aria-label={`Matriz de cotizaciones para ${
                  licitacionSeleccionada.numeroRequisicion ?? 'licitación'
                }`}
              >
                <thead>
                  <tr>
                    <th scope="col">Proveedor</th>
                    <th scope="col">Precio total</th>
                    <th scope="col">Flete</th>
                    <th scope="col">Plazo de entrega</th>
                    <th scope="col">Puntaje</th>
                    <th scope="col">Acción</th>
                  </tr>
                </thead>
                <tbody>
                  {cotizaciones.map((cot) => {
                    const precioTotal = calcularPrecioTotalCotizacion(cot, licitacionSeleccionada)
                    const proveedorAbrev = cot.proveedorId.slice(0, 8)
                    const estaConfirmando = confirmandoCotizacionId === cot.cotizacionId
                    const estaAdjudicando = adjudicandoCotizacionId === cot.cotizacionId
                    const hayOperacionEnVuelo = adjudicandoCotizacionId !== null

                    return (
                      <tr
                        key={cot.cotizacionId}
                        className={cot.esGanadora ? styles.filaDestacada : undefined}
                      >
                        <td>
                          <div className={styles.proveedorCelda}>
                            <span className={styles.proveedorId} title={cot.proveedorId}>
                              {proveedorAbrev}
                            </span>
                            {cot.esGanadora && (
                              <span className={styles.badgeGanadora}>★ Oferta destacada</span>
                            )}
                          </div>
                        </td>
                        <td className={styles.precioTotal}>{formatoPrecio(precioTotal)}</td>
                        <td>{cot.flete ? formatoPrecio(cot.flete) : '$ 0'}</td>
                        <td>
                          {cot.plazoEntregaDias ? `${cot.plazoEntregaDias} días` : 'Inmediato'}
                        </td>
                        <td className={styles.puntaje}>{cot.puntajeTotal} pts</td>
                        <td>
                          {estaConfirmando ? (
                            <div className={styles.contenedorConfirmacion}>
                              <p className={styles.textoConfirmacion}>
                                ¿Confirmás la adjudicación a este proveedor?
                              </p>
                              <div className={styles.accionesConfirmacion}>
                                <button
                                  type="button"
                                  className={styles.botonConfirmar}
                                  disabled={hayOperacionEnVuelo}
                                  onClick={() => void handleConfirmarAdjudicacion(cot)}
                                >
                                  {estaAdjudicando ? 'Adjudicando…' : 'Confirmar adjudicación'}
                                </button>
                                <button
                                  type="button"
                                  className={styles.botonCancelar}
                                  disabled={hayOperacionEnVuelo}
                                  onClick={() => setConfirmandoCotizacionId(null)}
                                >
                                  Cancelar
                                </button>
                              </div>
                            </div>
                          ) : (
                            <button
                              type="button"
                              className={styles.botonAdjudicar}
                              disabled={hayOperacionEnVuelo || esLicitacionAdjudicada}
                              onClick={() => {
                                setErrorAccion(null)
                                setMensajeExito(null)
                                setConfirmandoCotizacionId(cot.cotizacionId)
                              }}
                              aria-label={`Adjudicar a proveedor ${proveedorAbrev}`}
                            >
                              {esLicitacionAdjudicada ? 'Adjudicada' : 'Adjudicar'}
                            </button>
                          )}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </section>
  )
}
