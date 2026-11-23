import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { useAuth } from '../auth/useAuth'
import {
  listarLicitaciones,
  obtenerCalificacionProveedor,
  obtenerLicitacion,
  obtenerMisCotizaciones,
} from '../licitaciones/licitacionesService'
import type { LicitacionResponseDto, MiCotizacionDto } from '../licitaciones/licitacionesTipos'
import { formatearPesos } from '../presupuesto/formatoPesos'
import { fechaLarga } from '../requisiciones/formatoFecha'
import { cantidadConUnidad } from '../requisiciones/unidades'
import styles from './MisCotizacionesPage.module.css'

/** Texto y estilo de cada resultado que informa el servicio (estadoResultado). */
const RESULTADOS: Record<string, { texto: string; clase: string }> = {
  Enviada: { texto: 'Enviada, esperando el cierre', clase: 'enviada' },
  Gano: { texto: 'Licitación adjudicada', clase: 'gano' },
  NoGano: { texto: 'Licitación no adjudicada', clase: 'noGano' },
  LicitacionCerrada: { texto: 'Licitación cerrada, sin adjudicar todavía', clase: 'cerrada' },
}

type Filtro = 'todas' | 'pendientes' | 'en-curso' | 'adjudicadas' | 'no-adjudicadas'

const FILTROS: { id: Filtro; etiqueta: string; vacio: string }[] = [
  { id: 'todas', etiqueta: 'Todas', vacio: 'Todavía no enviaste cotizaciones.' },
  { id: 'pendientes', etiqueta: 'Pendientes', vacio: 'No tenés licitaciones pendientes de cotizar.' },
  { id: 'en-curso', etiqueta: 'En curso', vacio: 'No tenés cotizaciones en curso.' },
  { id: 'adjudicadas', etiqueta: 'Adjudicadas', vacio: 'Todavía no ganaste ninguna licitación.' },
  { id: 'no-adjudicadas', etiqueta: 'No adjudicadas', vacio: 'Ninguna de tus cotizaciones quedó sin adjudicar.' },
]

/** Los botones del filtro de estado: "Todas" no tiene botón, es no tener ningún filtro (Quitar filtros). */
const BOTONES_ESTADO = FILTROS.filter((f) => f.id !== 'todas')

/**
 * A qué grupo pertenece una cotización. Todo lo que todavía no se resolvió (licitación abierta o
 * cerrada sin adjudicar) cuenta como "en curso": nunca se le muestra como perdida.
 */
function grupoDe(c: MiCotizacionDto): Exclude<Filtro, 'todas' | 'pendientes'> {
  if (c.estadoResultado === 'Gano') return 'adjudicadas'
  if (c.estadoResultado === 'NoGano') return 'no-adjudicadas'
  return 'en-curso'
}

/** Una cotización se puede corregir solo mientras la licitación está abierta y el plazo no venció. */
function sePuedeCorregir(c: MiCotizacionDto): boolean {
  return c.estadoLicitacion === 'Abierta' && new Date(c.fechaLimiteCotizacion).getTime() > Date.now()
}

/** Los ítems donde el Proveedor ofreció otra presentación u otra cantidad que lo pedido (#229). */
function itemsConOtraPresentacion(c: MiCotizacionDto) {
  return c.items.filter((i) => Boolean(i.presentacionOfrecida?.trim()) || i.cantidadOfrecida != null)
}

/** Una fila del historial: una cotización ya enviada o una licitación abierta que todavía no cotizó. */
type Fila =
  | { tipo: 'cotizacion'; cotizacion: MiCotizacionDto }
  | { tipo: 'pendiente'; licitacion: LicitacionResponseDto }

/**
 * Portal del Proveedor: historial de sus cotizaciones con el resultado (en curso / ganó / no ganó),
 * filtros por estado, su calificación y, mientras la licitación sigue abierta, el acceso para corregir.
 * Las licitaciones abiertas que todavía no cotizó figuran como "Pendientes", con el botón para cotizar
 * (igual que el Historial de requisiciones del Comprador, donde figuran las que esperan su aprobación).
 */
export function MisCotizacionesPage() {
  const { usuario } = useAuth()
  if (usuario?.rol !== 'Proveedor') {
    return (
      <section className={styles.contenedor}>
        <Link to="/" className={styles.volver}>← Volver al inicio</Link>
        <h1>Historial de cotizaciones</h1>
        <p>Esta funcionalidad está disponible para proveedores.</p>
      </section>
    )
  }
  return <HistorialCotizaciones />
}

function HistorialCotizaciones() {
  const { usuario } = useAuth()
  const [parametros, setParametros] = useSearchParams()
  const [cotizaciones, setCotizaciones] = useState<MiCotizacionDto[]>([])
  const [pendientes, setPendientes] = useState<LicitacionResponseDto[]>([])
  const [errorPendientes, setErrorPendientes] = useState(false)
  const [cargando, setCargando] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [calificacion, setCalificacion] = useState<number | null>(null)
  const [cantidadCalificaciones, setCantidadCalificaciones] = useState(0)
  const [intento, setIntento] = useState(0)
  // Pliegos de las cotizaciones con otra presentación: de ahí salen el nombre del material y lo pedido.
  const [pliegos, setPliegos] = useState<Record<string, LicitacionResponseDto>>({})

  // El filtro vive en la dirección de la página (?estado=adjudicadas): sobrevive a recargar y se puede compartir.
  const pedido = parametros.get('estado')
  const filtro: Filtro = FILTROS.some((f) => f.id === pedido) ? (pedido as Filtro) : 'todas'

  function elegirFiltro(nuevo: Filtro) {
    setParametros(nuevo === 'todas' ? {} : { estado: nuevo }, { replace: true })
  }

  // Igual que en el Historial de requisiciones: tocar el botón activo lo desactiva.
  function alternarFiltro(id: Filtro) {
    elegirFiltro(filtro === id ? 'todas' : id)
  }

  useEffect(() => {
    let cancelado = false
    async function cargar() {
      try {
        // Las dos listas se piden a la vez. Si falla solo la de pendientes, el historial se ve igual.
        const [datos, abiertas] = await Promise.all([
          obtenerMisCotizaciones(),
          listarLicitaciones().catch(() => null),
        ])
        if (cancelado) return
        setCotizaciones(datos)
        setErrorPendientes(abiertas === null)
        setPendientes((abiertas ?? []).filter((l) => l.yaCotizada !== true))
      } catch {
        if (!cancelado) setError('No se pudieron cargar tus cotizaciones. Intentá nuevamente.')
      } finally {
        if (!cancelado) setCargando(false)
      }
    }
    void cargar()
    return () => {
      cancelado = true
    }
  }, [intento])

  // Solo se piden los pliegos de las cotizaciones que ofrecen otra presentación (son pocas). Si falla,
  // el detalle se muestra igual, sin el nombre del material.
  useEffect(() => {
    const ids = [
      ...new Set(
        cotizaciones.filter((c) => itemsConOtraPresentacion(c).length > 0).map((c) => c.licitacionId),
      ),
    ]
    let cancelado = false
    for (const id of ids) {
      obtenerLicitacion(id)
        .then((pliego) => {
          if (!cancelado) setPliegos((prev) => ({ ...prev, [id]: pliego }))
        })
        .catch(() => {})
    }
    return () => {
      cancelado = true
    }
  }, [cotizaciones])

  useEffect(() => {
    if (!usuario?.id) return
    let cancelado = false
    obtenerCalificacionProveedor(usuario.id)
      .then((c) => {
        if (cancelado) return
        setCalificacion(c.calificacion)
        setCantidadCalificaciones(c.cantidadCalificaciones)
      })
      .catch(() => {
        // 404 = todavía nadie lo calificó: no es un error para mostrar.
      })
    return () => {
      cancelado = true
    }
  }, [usuario?.id])

  // Las más recientes primero.
  const ordenadas = useMemo(
    () =>
      [...cotizaciones].sort(
        (a, b) => new Date(b.fechaCreacion).getTime() - new Date(a.fechaCreacion).getTime(),
      ),
    [cotizaciones],
  )

  // Las pendientes van primero (son lo que hay que hacer), la que cierra antes arriba.
  const pendientesOrdenadas = useMemo(
    () =>
      [...pendientes].sort(
        (a, b) => new Date(a.fechaLimiteCotizacion).getTime() - new Date(b.fechaLimiteCotizacion).getTime(),
      ),
    [pendientes],
  )

  const cantidades = useMemo(() => {
    const n: Record<Filtro, number> = {
      todas: ordenadas.length + pendientesOrdenadas.length,
      pendientes: pendientesOrdenadas.length,
      'en-curso': 0,
      adjudicadas: 0,
      'no-adjudicadas': 0,
    }
    for (const c of ordenadas) n[grupoDe(c)] += 1
    return n
  }, [ordenadas, pendientesOrdenadas])

  const filasPendientes: Fila[] = pendientesOrdenadas.map((licitacion) => ({ tipo: 'pendiente', licitacion }))
  const filasCotizaciones: Fila[] = ordenadas
    .filter((c) => filtro === 'todas' || grupoDe(c) === filtro)
    .map((cotizacion) => ({ tipo: 'cotizacion', cotizacion }))
  const visibles: Fila[] = [
    ...(filtro === 'todas' || filtro === 'pendientes' ? filasPendientes : []),
    ...(filtro === 'pendientes' ? [] : filasCotizaciones),
  ]
  const hayAlgo = cantidades.todas > 0
  const mensajeVacio = FILTROS.find((f) => f.id === filtro)?.vacio ?? ''

  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to="/" className={styles.volver}>
        ← Volver al inicio
      </Link>
      <h1>Historial de cotizaciones</h1>

      <p className={styles.calificacion}>
        {calificacion === null
          ? 'Todavía no tenés calificación: aparece cuando Compras evalúe tus entregas.'
          : `Tu calificación: ${calificacion.toFixed(1)} de 5 (${cantidadCalificaciones} ${
              cantidadCalificaciones === 1 ? 'evaluación' : 'evaluaciones'
            })`}
      </p>

      {cargando && <p role="status">Cargando…</p>}

      {!cargando && error && (
        <div>
          <p role="alert">{error}</p>
          <button
            type="button"
            onClick={() => {
              setError(null)
              setCargando(true)
              setIntento((n) => n + 1)
            }}
          >
            Reintentar
          </button>
        </div>
      )}

      {!cargando && !error && errorPendientes && (
        <p role="alert">No se pudieron cargar las licitaciones pendientes de cotizar.</p>
      )}

      {!cargando && !error && !hayAlgo && <p>Todavía no enviaste cotizaciones.</p>}

      {!cargando && !error && hayAlgo && (
        <div className={styles.filtros} role="group" aria-label="Filtros del historial">
          <div className={styles.filtro}>
            <span id="filtro-estado-titulo" className={styles.tituloFiltro}>
              Estado
            </span>
            <div className={styles.botonesEstado} role="group" aria-labelledby="filtro-estado-titulo">
              {BOTONES_ESTADO.map((f) => (
                <button
                  key={f.id}
                  type="button"
                  className={styles.botonEstado}
                  aria-pressed={filtro === f.id}
                  onClick={() => alternarFiltro(f.id)}
                >
                  {f.etiqueta}
                </button>
              ))}
            </div>
          </div>
          <button
            type="button"
            className={styles.quitar}
            onClick={() => elegirFiltro('todas')}
            disabled={filtro === 'todas'}
          >
            Quitar filtros
          </button>
        </div>
      )}

      {!cargando && !error && hayAlgo && visibles.length === 0 && (
        <p className={styles.vacio}>{mensajeVacio}</p>
      )}

      <ul className={styles.lista}>
        {visibles.map((fila) => {
          if (fila.tipo === 'pendiente') {
            const lic = fila.licitacion
            const cantItems = lic.items?.length ?? 0
            return (
              <li key={`pendiente-${lic.licitacionId}`} className={styles.tarjeta}>
                <div className={styles.encabezado}>
                  <h2>{lic.numeroRequisicion ?? 'Licitación'}</h2>
                  <span className={`${styles.resultado} ${styles.pendiente}`}>Pendiente de cotizar</span>
                </div>
                <dl className={styles.datos}>
                  <div>
                    <dt>Materiales</dt>
                    <dd>{cantItems} {cantItems === 1 ? 'ítem' : 'ítems'}</dd>
                  </div>
                  <div>
                    <dt>Cierre de la licitación</dt>
                    <dd>{fechaLarga(lic.fechaLimiteCotizacion)}</dd>
                  </div>
                </dl>
                <div className={styles.acciones}>
                  <Link
                    to={`/proveedores/licitaciones/${lic.licitacionId}`}
                    className={`accion-grande ${styles.cotizar}`}
                  >
                    Cotizar
                  </Link>
                </div>
              </li>
            )
          }
          const c = fila.cotizacion
          const resultado = RESULTADOS[c.estadoResultado] ?? { texto: c.estadoResultado, clase: 'cerrada' }
          const itemsOtraPresentacion = itemsConOtraPresentacion(c)
          const hayPresentacionDistinta = c.items.some((i) => Boolean(i.presentacionOfrecida?.trim()))
          // La marca la calcula el servicio (#190): acá solo se muestra lo que devolvió.
          const hayCoberturaParcial = Boolean(c.coberturaParcial) || c.items.some((i) => i.coberturaParcial)
          return (
            <li key={c.cotizacionId} className={styles.tarjeta}>
              <div className={styles.encabezado}>
                <h2>{c.numeroRequisicion}</h2>
                <div className={styles.etiquetas}>
                  <span className={`${styles.resultado} ${styles[resultado.clase]}`}>{resultado.texto}</span>
                  {hayPresentacionDistinta && (
                    <span className={`${styles.resultado} ${styles.presentacionDistinta}`}>Presentación distinta</span>
                  )}
                  {hayCoberturaParcial && (
                    <span className={`${styles.resultado} ${styles.coberturaParcial}`}>Cobertura parcial</span>
                  )}
                </div>
              </div>
              <dl className={styles.datos}>
                <div className={styles.total}>
                  <dt>Total ofertado</dt>
                  <dd>
                    <strong>{formatearPesos(c.costoTotal)}</strong>
                    <span className={styles.nota}>flete incluido</span>
                  </dd>
                </div>
                <div>
                  <dt>Plazo de entrega</dt>
                  <dd>
                    {c.plazoEntregaDias} {c.plazoEntregaDias === 1 ? 'día' : 'días'}
                  </dd>
                </div>
                <div>
                  <dt>Enviada el</dt>
                  <dd>{fechaLarga(c.fechaCreacion)}</dd>
                </div>
                <div>
                  <dt>Cierre de la licitación</dt>
                  <dd>{fechaLarga(c.fechaLimiteCotizacion)}</dd>
                </div>
              </dl>
              {itemsOtraPresentacion.length > 0 && (
                <div className={styles.ofrecido}>
                  <h3>Lo que ofreciste distinto de lo pedido</h3>
                  <ul>
                    {itemsOtraPresentacion.map((it) => {
                      const pedido = pliegos[c.licitacionId]?.items.find((p) => p.articuloId === it.articuloId)
                      const cantidad = it.cantidadOfrecida ?? null
                      const presentacion = it.presentacionOfrecida?.trim()
                      const total = it.subtotalItem ?? (cantidad !== null ? cantidad * it.precioUnitario : null)
                      const queOfreciste =
                        cantidad !== null
                          ? `${cantidad} ${presentacion ? `× ${presentacion}` : 'en la presentación pedida'}`
                          : (presentacion ?? '')
                      return (
                        <li key={it.articuloId}>
                          <strong>{pedido?.descripcion ?? 'Material ofrecido'}</strong>
                          {pedido && <span>Pedido: {cantidadConUnidad(pedido.cantidad, pedido.unidadMedida)}</span>}
                          <span>
                            Ofreciste: {queOfreciste} a {formatearPesos(it.precioUnitario)} c/u
                            {total !== null && <> · Total: {formatearPesos(total)}</>}
                          </span>
                          {it.coberturaParcial && (
                            <span className={styles.avisoParcial}>Esto no cubre todo lo pedido.</span>
                          )}
                        </li>
                      )
                    })}
                  </ul>
                </div>
              )}
              {(sePuedeCorregir(c) || c.estadoResultado === 'Gano') && (
                <div className={styles.acciones}>
                  {sePuedeCorregir(c) && (
                    <Link
                      to={`/proveedores/licitaciones/${c.licitacionId}?corregir=${c.cotizacionId}`}
                      className={styles.accion}
                    >
                      Editar cotización
                    </Link>
                  )}
                  {c.estadoResultado === 'Gano' && (
                    <Link to="/proveedores/mis-ordenes" className={`${styles.accion} ${styles.accionPrincipal}`}>
                      Ver mis órdenes
                    </Link>
                  )}
                </div>
              )}
            </li>
          )
        })}
      </ul>
    </section>
  )
}
