import { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { ApiError } from '../api/ApiError'
import {
  calcularPrecioTotalCotizacion,
  calcularTotalConFlete,
  compararConPrecioPrevio,
  desglosarCotizacion,
  estrellasDePuntaje,
  formatoPrecio,
  marcasDeComparacion,
  ordenarCotizaciones,
  type OrdenCotizaciones,
  tieneCoberturaParcial,
  tienePresentacionDistinta,
} from '../licitaciones/cotizacionesCalculos'
import {
  adjudicarLicitacion,
  listarCotizaciones,
  listarLicitaciones,
  listarOrdenesCompra,
  obtenerPreciosHistoricos,
} from '../licitaciones/licitacionesService'
import type { PrecioPrevio } from '../licitaciones/cotizacionesCalculos'
import type {
  CotizacionResponseDto,
  LicitacionResponseDto,
} from '../licitaciones/licitacionesTipos'
import { AccionesLicitacionAbierta } from '../licitaciones/AccionesLicitacionAbierta'
import { AvisoPorLicitar } from '../licitaciones/RequisicionesPorLicitar'
import { fechaLarga } from '../requisiciones/formatoFecha'
import { cantidadConUnidad } from '../requisiciones/unidades'
import styles from './LicitacionesComprasPage.module.css'

const CRITERIOS = [
  { clave: 'precio', etiqueta: 'Precio' },
  { clave: 'flete', etiqueta: 'Flete' },
  { clave: 'plazo', etiqueta: 'Plazo de entrega' },
  { clave: 'calificacion', etiqueta: 'Calificación del proveedor' },
] as const

const OPCIONES_ORDEN: Array<{ valor: OrdenCotizaciones; etiqueta: string }> = [
  { valor: 'mejor-puntaje', etiqueta: 'Mejor puntaje' },
  { valor: 'precio-asc', etiqueta: 'Precio: menor a mayor' },
  { valor: 'precio-desc', etiqueta: 'Precio: mayor a menor' },
  { valor: 'flete-asc', etiqueta: 'Flete: más barato' },
  { valor: 'flete-desc', etiqueta: 'Flete: más caro' },
  { valor: 'plazo-asc', etiqueta: 'Plazo de entrega: más rápido' },
  { valor: 'plazo-desc', etiqueta: 'Plazo de entrega: más lento' },
  { valor: 'calificacion-desc', etiqueta: 'Calificación del proveedor: mejor' },
  { valor: 'calificacion-asc', etiqueta: 'Calificación del proveedor: peor' },
]

function ordenValido(valor: string | null): OrdenCotizaciones {
  return OPCIONES_ORDEN.some((opcion) => opcion.valor === valor)
    ? (valor as OrdenCotizaciones)
    : 'mejor-puntaje'
}

/** Qué puntaje del desglose del servicio corresponde a cada criterio. */
const CLAVES_DESGLOSE = {
  precio: 'puntajePrecio',
  flete: 'puntajeFlete',
  plazo: 'puntajePlazo',
  calificacion: 'puntajeCalificacion',
} as const

/** 12,5 / 8 / 92,25: hasta dos decimales, con coma. */
function formatoNumero(valor: number): string {
  return valor.toLocaleString('es-AR', { maximumFractionDigits: 2 })
}

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
  const orden = ordenValido(searchParams.get('orden'))

  const [licitaciones, setLicitaciones] = useState<LicitacionResponseDto[]>([])
  const [cargandoLicitaciones, setCargandoLicitaciones] = useState(true)
  const [errorLicitaciones, setErrorLicitaciones] = useState<string | null>(null)
  const [intentoLicitaciones, setIntentoLicitaciones] = useState(0)

  const [cotizaciones, setCotizaciones] = useState<CotizacionResponseDto[]>([])
  // Lo que se pagó la última vez por cada material del pliego. Si no se puede traer, la comparación simplemente no se muestra.
  const [preciosPrevios, setPreciosPrevios] = useState<Map<string, PrecioPrevio>>(new Map())
  const [cargandoCotizaciones, setCargandoCotizaciones] = useState(false)
  const [errorCotizaciones, setErrorCotizaciones] = useState<string | null>(null)
  const [intentoCotizaciones, setIntentoCotizaciones] = useState(0)
  const [ofertaAdjudicada, setOfertaAdjudicada] = useState<{
    licitacionId: string
    cotizacionId: string
  } | null>(null)
  const [confirmandoCotizacionId, setConfirmandoCotizacionId] = useState<string | null>(null)
  const [adjudicandoCotizacionId, setAdjudicandoCotizacionId] = useState<string | null>(null)
  const [mensajeExito, setMensajeExito] = useState<string | null>(null)
  const [ordenCompraGeneradaId, setOrdenCompraGeneradaId] = useState<string | null>(null)
  const [errorAccion, setErrorAccion] = useState<string | null>(null)

  const peticionEnVueloRef = useRef(false)
  // Al elegir una licitación, la pantalla baja sola a sus cotizaciones y el foco queda en el título
  // (si no, en el celular el bloque aparece muy abajo y no se nota que pasó algo).
  const tituloCotizacionesRef = useRef<HTMLHeadingElement>(null)
  const irACotizacionesRef = useRef(false)

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

  // La orden adjudicada conserva el cotizacionId ganador; sirve para identificarla al reabrir la pantalla.
  useEffect(() => {
    const licitacion = licitacionSeleccionada
    if (!licitacion || licitacion.estado !== 'Adjudicada') return
    let cancelado = false
    listarOrdenesCompra({ licitacionId: licitacion.licitacionId })
      .then((ordenes) => {
        if (cancelado) return
        const orden = ordenes.find((item) => item.licitacionId === licitacion.licitacionId)
        if (orden) {
          setOfertaAdjudicada({ licitacionId: licitacion.licitacionId, cotizacionId: orden.cotizacionId })
        } else {
          setOfertaAdjudicada((actual) => actual?.licitacionId === licitacion.licitacionId ? actual : null)
        }
      })
      .catch(() => {})
    return () => {
      cancelado = true
    }
  }, [licitacionSeleccionada])

  // Lo que se pagó antes por los materiales del pliego (para comparar cada oferta con la última compra)
  const idsDeMateriales = licitacionSeleccionada?.items.map((m) => m.articuloId).join(',') ?? ''
  useEffect(() => {
    let cancelado = false
    // Sin licitación elegida no hay nada que pedir. No se vacía el mapa acá (setState directo dentro del efecto):
    // se consulta por articuloId, así que lo que quedó de otra licitación no se usa.
    if (!idsDeMateriales) return
    obtenerPreciosHistoricos(idsDeMateriales.split(','))
      .then((lista) => {
        if (!cancelado) setPreciosPrevios(new Map(lista.map((p) => [p.articuloId, p])))
      })
      .catch(() => {
        if (!cancelado) setPreciosPrevios(new Map())
      })
    return () => {
      cancelado = true
    }
  }, [idsDeMateriales])

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

  function irATituloDeCotizaciones() {
    const titulo = tituloCotizacionesRef.current
    if (!titulo) return
    const sinAnimacion = window.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches ?? false
    titulo.scrollIntoView?.({ behavior: sinAnimacion ? 'auto' : 'smooth', block: 'start' })
    titulo.focus({ preventScroll: true })
  }

  // Cuando la licitación elegida ya está en pantalla, se baja a sus cotizaciones (solo si fue un toque del usuario).
  useEffect(() => {
    if (!irACotizacionesRef.current || !licitacionSeleccionada) return
    irACotizacionesRef.current = false
    irATituloDeCotizaciones()
  }, [licitacionSeleccionada])

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
    if (id === licitacionIdParam) {
      // Ya estaba elegida: no cambia nada en la URL, así que se baja directamente.
      irATituloDeCotizaciones()
      return
    }
    irACotizacionesRef.current = true
    setSearchParams((actuales) => {
      actuales.set('licitacionId', id)
      return actuales
    })
  }

  async function handleConfirmarAdjudicacion(cotizacion: CotizacionResponseDto) {
    if (!licitacionSeleccionada) return
    if (peticionEnVueloRef.current) return
    peticionEnVueloRef.current = true
    setAdjudicandoCotizacionId(cotizacion.cotizacionId)
    setErrorAccion(null)
    setMensajeExito(null)
    setOrdenCompraGeneradaId(null)

    try {
      const ordenCompra = await adjudicarLicitacion(
        licitacionSeleccionada.licitacionId,
        cotizacion.cotizacionId,
      )
      const ordenId = typeof ordenCompra?.id === 'string' ? ordenCompra.id.trim() : ''
      setMensajeExito(ordenId
        ? `¡Licitación adjudicada con éxito! Se emitió la Orden de Compra ${ordenId}.`
        : '¡Licitación adjudicada con éxito!')
      setOrdenCompraGeneradaId(ordenId || null)
      setConfirmandoCotizacionId(null)
      setOfertaAdjudicada({
        licitacionId: licitacionSeleccionada.licitacionId,
        cotizacionId: ordenCompra.cotizacionId,
      })
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
  // El ID de la oferta adjudicada se obtiene de la orden persistida, o de la respuesta del POST recién realizado.
  const cotizacionAdjudicadaId =
    ofertaAdjudicada !== null &&
    esLicitacionAdjudicada &&
    ofertaAdjudicada.licitacionId === licitacionSeleccionada?.licitacionId
      ? ofertaAdjudicada.cotizacionId
      : null
  const marcasPorCotizacion = licitacionSeleccionada
    ? marcasDeComparacion(cotizaciones, licitacionSeleccionada)
    : new Map<string, string[]>()

  const cotizacionesOrdenadas = licitacionSeleccionada
    ? ordenarCotizaciones(cotizaciones, orden, licitacionSeleccionada)
    : cotizaciones

  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to="/" className={styles.volver}>
        ← Volver al inicio
      </Link>
      <h1>Licitaciones y cotizaciones</h1>

      {/* Solo cuando la lista de licitaciones cargó: si no, parecería que ninguna requisición fue licitada. */}
      {!cargandoLicitaciones && !errorLicitaciones && (
        <AvisoPorLicitar licitaciones={licitaciones} />
      )}
      <p className="intro">
        Consultá las licitaciones abiertas, compará cotizaciones recibidas y adjudicá las compras.
      </p>

      {licitacionSeleccionada && !cargandoCotizaciones && !errorCotizaciones && (
        <p role="status">
          {licitacionSeleccionada.estado === 'Adjudicada'
            ? 'Esta licitación ya fue adjudicada. El siguiente paso es seguir la Orden de compra.'
            : licitacionSeleccionada.estado === 'Abierta' && cotizaciones.length === 0
              ? 'La licitación está abierta y todavía no tiene cotizaciones. Esperá nuevas ofertas; cuando lleguen, comparalas y adjudicá.'
              : licitacionSeleccionada.estado === 'Abierta'
                ? `${cotizaciones.length} cotización${cotizaciones.length === 1 ? '' : 'es'} recibida${cotizaciones.length === 1 ? '' : 's'}. Comparalas y adjudicá para emitir la Orden de compra.`
                : `La licitación está ${licitacionSeleccionada.estado.toLowerCase()}. Consultá Órdenes de compra para continuar el seguimiento.`}
          {licitacionSeleccionada.estado === 'Adjudicada' && <> <Link to="/compras/ordenes">Ir a Órdenes de compra</Link></>}
        </p>
      )}

      {mensajeExito && (
        <p className={styles.exito} role="status">
          {mensajeExito}
          {ordenCompraGeneradaId && <> <Link to={`/compras/ordenes?ordenCompraId=${encodeURIComponent(ordenCompraGeneradaId)}`}>Ver la orden de compra</Link></>}
          {!ordenCompraGeneradaId && <span> No recibimos un identificador válido de la orden. Podés buscarla en <Link to="/compras/ordenes">Órdenes de compra</Link>.</span>}
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
            <h2 ref={tituloCotizacionesRef} tabIndex={-1}>
              Cotizaciones para{' '}
              <span className={styles.numeroTitulo}>{licitacionSeleccionada.numeroRequisicion ?? 'la licitación'}</span>
            </h2>
            {esLicitacionAdjudicada && (
              <span className={`${styles.estadoBadge} ${styles.estadoAdjudicada}`}>
                Licitación ya adjudicada
              </span>
            )}
          </div>

          {licitacionSeleccionada.estado === 'Abierta' && (
            <AccionesLicitacionAbierta
              key={licitacionSeleccionada.licitacionId}
              licitacion={licitacionSeleccionada}
              onActualizada={(actualizada) =>
                setLicitaciones((actuales) =>
                  actuales.map((l) => (l.licitacionId === actualizada.licitacionId ? { ...l, ...actualizada } : l)),
                )
              }
            />
          )}

          <section className={styles.resumenPliego} aria-label="Resumen del pliego">
            <h3 className={styles.tituloResumen}>Qué se pidió</h3>
            <ul className={styles.listaPliego}>
              {licitacionSeleccionada.items.map((material) => (
                <li key={material.articuloId}>
                  {cantidadConUnidad(material.cantidad, material.unidadMedida)} de{' '}
                  {material.descripcion}
                </li>
              ))}
            </ul>
            <p className={styles.datoResumen}>
              Cotizan hasta el {fechaLarga(licitacionSeleccionada.fechaLimiteCotizacion)}
              {!cargandoCotizaciones && !errorCotizaciones && (
                <>
                  {' · '}
                  <strong>
                    {cotizaciones.length === 1
                      ? '1 oferta recibida'
                      : `${cotizaciones.length} ofertas recibidas`}
                  </strong>
                </>
              )}
            </p>
          </section>

          <label className={styles.selectorOrden}>
            Ordenar ofertas
            <select
              aria-label="Ordenar ofertas"
              value={orden}
              onChange={(evento) => {
                const nuevoOrden = evento.currentTarget.value as OrdenCotizaciones
                setSearchParams((actuales) => {
                  actuales.set('orden', nuevoOrden)
                  return actuales
                })
              }}
            >
              {OPCIONES_ORDEN.map((opcion) => (
                <option key={opcion.valor} value={opcion.valor}>{opcion.etiqueta}</option>
              ))}
            </select>
          </label>

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
            <ul
              className={styles.listaCotizaciones}
              aria-label={`Cotizaciones recibidas para ${
                licitacionSeleccionada.numeroRequisicion ?? 'licitación'
              }`}
            >
              {cotizacionesOrdenadas.map((cot) => {
                // Si el servicio ya calculó los importes (contrato), se usan esos; si no, se calculan acá.
                const precioTotal =
                  cot.subtotalItems ?? calcularPrecioTotalCotizacion(cot, licitacionSeleccionada)
                const totalConFlete =
                  cot.costoTotal ?? calcularTotalConFlete(cot, licitacionSeleccionada)
                const marcas = marcasPorCotizacion.get(cot.cotizacionId) ?? []
                const lineas = desglosarCotizacion(cot, licitacionSeleccionada)
                // Para que no se compare como si fuera lo mismo (#229). No se descarta ninguna oferta.
                const presentacionDistinta = tienePresentacionDistinta(cot)
                const coberturaParcial = tieneCoberturaParcial(cot)
                const proveedorAbrev = cot.proveedorId.slice(0, 8)
                // Si el servicio informa el nombre se muestra; si no, el comienzo del identificador.
                const nombreProveedor = cot.proveedorNombre?.trim() || proveedorAbrev
                const estaConfirmando = confirmandoCotizacionId === cot.cotizacionId
                const estaAdjudicando = adjudicandoCotizacionId === cot.cotizacionId
                const hayOperacionEnVuelo = adjudicandoCotizacionId !== null

                return (
                  <li
                    key={cot.cotizacionId}
                    className={`${styles.tarjetaCotizacion} ${
                      cot.esGanadora ? styles.cotizacionDestacada : ''
                    }`}
                  >
                    <div className={styles.cotizacionEncabezado}>
                      <span className={styles.etiquetaProveedor}>Proveedor</span>
                      <span className={styles.proveedorId} title={cot.proveedorId}>
                        {nombreProveedor}
                      </span>
                      {cot.esGanadora && (
                        <span className={styles.badgeGanadora}>★ Oferta destacada</span>
                      )}
                      {cot.cotizacionId === cotizacionAdjudicadaId && (
                        <span className={styles.badgeAdjudicada}>Adjudicada</span>
                      )}
                      {presentacionDistinta && (
                        <span className={styles.insigniaPresentacion}>Presentación distinta</span>
                      )}
                      {coberturaParcial && <span className={styles.insigniaCobertura}>Cobertura parcial</span>}
                    </div>
                    {marcas.length > 0 && (
                      <ul className={styles.marcasComparacion} aria-label="Esta oferta se destaca en">
                        {marcas.map((marca) => (
                          <li key={marca} className={styles.marca}>
                            {marca}
                          </li>
                        ))}
                      </ul>
                    )}
                    <div className={styles.bloquePrecio}>
                      <p className={styles.etiquetaDato}>Precio total</p>
                      <p className={styles.precioTotal}>{formatoPrecio(precioTotal)}</p>
                    </div>
                    <dl className={styles.datosCotizacion}>
                      <div>
                        <dt>Total con flete</dt>
                        <dd>{formatoPrecio(totalConFlete)}</dd>
                      </div>
                      <div>
                        <dt>Flete</dt>
                        <dd>{cot.flete ? formatoPrecio(cot.flete) : '$ 0'}</dd>
                      </div>
                      <div>
                        <dt>Plazo de entrega</dt>
                        <dd>{cot.plazoEntregaDias ? `${cot.plazoEntregaDias} días` : 'Inmediato'}</dd>
                      </div>
                      <div className={styles.datoCalificacion}>
                        <dt>Calificación de la oferta</dt>
                        <dd className={styles.puntaje}>{formatoNumero(cot.puntajeTotal)} de 100</dd>
                      </div>
                      {cot.calificacionProveedor != null && (
                        <div className={styles.datoCalificacion}>
                          <dt>Reputación del proveedor</dt>
                          <dd>
                            {formatoNumero(cot.calificacionProveedor)} ★
                            {cot.cantidadCalificacionesProveedor != null && ` con ${cot.cantidadCalificacionesProveedor}`}
                          </dd>
                        </div>
                      )}
                      {cot.calificacionProveedor == null && (
                        <div className={styles.datoCalificacion}>
                          <dt>Reputación del proveedor</dt>
                          <dd>Sin calificaciones</dd>
                        </div>
                      )}
                    </dl>
                    <div className={styles.bloqueDetalles}>
                    <details className={styles.detalleItems} open={presentacionDistinta || coberturaParcial || undefined}>
                      <summary>Ver detalle por ítem</summary>
                      <ul className={styles.listaDetalle}>
                        {lineas.map((linea) => (
                          <li key={linea.articuloId}>
                            <span>
                              {cantidadConUnidad(linea.cantidad, linea.unidadMedida)} de{' '}
                              {linea.descripcion}
                            </span>
                            {(linea.presentacionOfrecida || linea.cantidadOfrecida !== undefined) && (
                              <span className={styles.ofertaAlternativa}>
                                Ofrece:{' '}
                                {linea.cantidadOfrecida !== undefined
                                  ? `${linea.cantidadOfrecida} ${
                                      linea.presentacionOfrecida
                                        ? `× ${linea.presentacionOfrecida}`
                                        : 'en la presentación pedida'
                                    }`
                                  : linea.presentacionOfrecida}
                              </span>
                            )}
                            <span className={styles.lineaPrecio}>
                              {linea.precioUnitario === undefined
                                ? 'No cotizó este material'
                                : `${formatoPrecio(linea.precioUnitario)} c/u = ${formatoPrecio(linea.subtotal)}`}
                              {/* Con otra presentación el precio no es por la misma unidad: no se compara con la última compra. */}
                              {linea.precioUnitario !== undefined && !linea.presentacionOfrecida && (() => {
                                const previo = preciosPrevios.get(linea.articuloId)
                                const comparacion = compararConPrecioPrevio(linea.precioUnitario, previo)
                                return comparacion && previo ? (
                                  <small className={styles.comparacionPrevia}>
                                    {' '}· {comparacion.texto} ({formatoPrecio(previo.ultimoPrecioUnitario)} c/u)
                                  </small>
                                ) : null
                              })()}
                            </span>
                            {linea.coberturaParcial && (
                              <span className={styles.avisoParcial}>Esto no cubre todo lo pedido.</span>
                            )}
                          </li>
                        ))}
                      </ul>
                    </details>
                      {cot.desgloseScoring ? (
                        <details className={styles.detalleItems}>
                          <summary>Calificación por criterio</summary>
                          <p className={styles.ayudaEstrellas}>
                            Las estrellas comparan esta oferta con las demás en cada criterio.
                          </p>
                          <ul className={styles.listaDetalle}>
                            {CRITERIOS.map(({ clave, etiqueta }) => {
                              const estrellas = estrellasDePuntaje(cot.desgloseScoring![CLAVES_DESGLOSE[clave]])
                              return (
                                <li key={clave}>
                                  <span>{etiqueta}</span>
                                  <span className={styles.lineaEstrellas}>
                                    <span
                                      role="img"
                                      aria-label={`${estrellas} de 5 estrellas`}
                                      className={styles.estrellas}
                                    >
                                      {'★'.repeat(estrellas)}
                                      <span className={styles.estrellaVacia}>
                                        {'★'.repeat(5 - estrellas)}
                                      </span>
                                    </span>
                                  </span>
                                </li>
                              )
                            })}
                          </ul>
                        </details>
                      ) : (
                        cot.desglosePuntaje && (
                        <details className={styles.detalleItems}>
                          <summary>Calificación por criterio</summary>
                          <ul className={styles.listaDetalle}>
                            {CRITERIOS.map(({ clave, etiqueta }) => (
                              <li key={clave}>
                                <span>{etiqueta}</span>
                                <span className={styles.lineaPrecio}>
                                  {formatoNumero(cot.desglosePuntaje![clave])} pts
                                </span>
                              </li>
                            ))}
                          </ul>
                        </details>
                        )
                      )}
                    </div>
                    <p className={styles.fechaCotizacion}>
                      Cotizó el {fechaLarga(cot.fechaCreacion)}
                    </p>
                    <div className={styles.accionCotizacion}>
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
                        className={
                          !esLicitacionAdjudicada
                            ? styles.botonAdjudicar
                            : cot.cotizacionId === cotizacionAdjudicadaId
                              ? `${styles.botonAdjudicar} ${styles.botonAdjudicada}`
                              : `${styles.botonAdjudicar} ${styles.botonCerrada}`
                        }
                        disabled={hayOperacionEnVuelo || esLicitacionAdjudicada}
                        onClick={() => {
                          setErrorAccion(null)
                          setMensajeExito(null)
                          setConfirmandoCotizacionId(cot.cotizacionId)
                        }}
                        aria-label={`Adjudicar a proveedor ${nombreProveedor}`}
                      >
                        {!esLicitacionAdjudicada
                          ? 'Adjudicar'
                          : cot.cotizacionId === cotizacionAdjudicadaId
                            ? '✓ Adjudicada'
                            : cotizacionAdjudicadaId
                              ? 'No adjudicada'
                              : 'Licitación adjudicada'}
                      </button>
                    )}
                    </div>
                  </li>
                )
              })}
            </ul>
          )}
        </div>
      )}
    </section>
  )
}
