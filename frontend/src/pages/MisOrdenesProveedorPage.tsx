import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { ApiError } from '../api/ApiError'
import { despacharOrdenCompra, listarOrdenesCompra } from '../licitaciones/licitacionesService'
import type { OrdenCompraListadoDto } from '../licitaciones/licitacionesTipos'
import { fechaLarga } from '../requisiciones/formatoFecha'
import styles from './MisOrdenesProveedorPage.module.css'

const MAX_OBSERVACION = 200

/** Código corto para nombrar una orden en pantalla (el mismo que ve Compras): los primeros 8 caracteres del id. */
function codigoDeOrden(id: string): string {
  return id.slice(0, 8).toUpperCase()
}

/** Primero las que faltan despachar (lo que el Proveedor tiene que hacer) y, dentro de cada grupo, las más nuevas. */
function ordenarParaDespachar(ordenes: OrdenCompraListadoDto[]): OrdenCompraListadoDto[] {
  return [...ordenes].sort((a, b) => {
    if (a.fueDespachada !== b.fueDespachada) return a.fueDespachada ? 1 : -1
    return new Date(b.fechaEmision).getTime() - new Date(a.fechaEmision).getTime()
  })
}

/** Fecha de hoy ("2026-11-21") en UTC: el servicio compara la fecha estimada contra hoy en UTC. */
function hoyUtc(): string {
  return new Date().toISOString().slice(0, 10)
}

/** Devuelve el mensaje de error del formulario, o null si está todo bien. */
function validarDespacho(fecha: string, observacion: string): string | null {
  if (fecha && fecha < hoyUtc()) return 'La fecha estimada de llegada no puede ser anterior a hoy.'
  if (observacion.trim().length > MAX_OBSERVACION) {
    return `La observación no puede superar los ${MAX_OBSERVACION} caracteres.`
  }
  return null
}

function mensajeDeErrorDespacho(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 400) return err.detail ?? 'Revisá los datos del despacho.'
    if (err.status === 403) return 'No podés despachar esta orden: solo la puede despachar el proveedor al que se la compraron.'
    if (err.status === 404) return err.detail ?? 'La orden de compra ya no existe. Se actualizó la lista.'
  }
  return 'No se pudo conectar con el servidor. Intentá nuevamente.'
}

/**
 * Portal del Proveedor: las órdenes de compra que ganó. Cuando envía la mercadería toca "Lo despaché",
 * puede indicar cuándo llega y dejar una nota, y de ahí en más Compras y el Capataz lo ven (#223).
 * El servicio solo le devuelve las órdenes propias.
 */
export function MisOrdenesProveedorPage() {
  const [ordenes, setOrdenes] = useState<OrdenCompraListadoDto[]>([])
  const [cargando, setCargando] = useState(true)
  const [error, setError] = useState<string | null>(null)
  // Cada vez que cambia "intento" se vuelve a pedir la lista (refresco tras un 404).
  const [intento, setIntento] = useState(0)

  // Formulario de despacho abierto (una orden a la vez) y sus campos.
  const [abiertaId, setAbiertaId] = useState<string | null>(null)
  const [fecha, setFecha] = useState('')
  const [observacion, setObservacion] = useState('')
  const [errorForm, setErrorForm] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [mensajeExito, setMensajeExito] = useState<string | null>(null)
  // Guard inmediato contra el doble toque: el estado recién cambia en el próximo render.
  const enVueloRef = useRef(false)

  useEffect(() => {
    let cancelado = false
    listarOrdenesCompra()
      .then((datos) => {
        if (!cancelado) {
          setOrdenes(datos)
          setError(null)
        }
      })
      .catch(() => {
        if (!cancelado) setError('No se pudieron cargar tus órdenes. Intentá nuevamente.')
      })
      .finally(() => {
        if (!cancelado) setCargando(false)
      })
    return () => {
      cancelado = true
    }
  }, [intento])

  function abrirFormulario(id: string) {
    if (enVueloRef.current) return
    setMensajeExito(null)
    setErrorForm(null)
    setFecha('')
    setObservacion('')
    setAbiertaId(id)
  }

  function cerrarFormulario() {
    if (enVueloRef.current) return
    setAbiertaId(null)
    setErrorForm(null)
  }

  async function confirmarDespacho(evento: FormEvent<HTMLFormElement>, orden: OrdenCompraListadoDto) {
    evento.preventDefault()
    if (enVueloRef.current) return

    const problema = validarDespacho(fecha, observacion)
    if (problema) {
      setErrorForm(problema)
      return
    }

    enVueloRef.current = true
    setEnviando(true)
    setErrorForm(null)
    try {
      const actualizada = await despacharOrdenCompra(orden.id, {
        // Mediodía UTC: así la fecha elegida se ve igual en cualquier zona horaria.
        fechaEstimadaLlegada: fecha ? `${fecha}T12:00:00Z` : undefined,
        observacion: observacion.trim() || undefined,
      })
      setOrdenes((actuales) => actuales.map((o) => (o.id === orden.id ? { ...o, ...actualizada } : o)))
      setMensajeExito(
        `Avisaste que despachaste la orden ${codigoDeOrden(orden.id)}. Compras ya lo ve y el Capataz podrá recibirla.`,
      )
      setAbiertaId(null)
    } catch (err) {
      setErrorForm(mensajeDeErrorDespacho(err))
      // Si la orden ya no existe, la lista en pantalla quedó vieja: se actualiza.
      if (err instanceof ApiError && err.status === 404) setIntento((n) => n + 1)
    } finally {
      enVueloRef.current = false
      setEnviando(false)
    }
  }

  const ordenadas = ordenarParaDespachar(ordenes)
  const pendientes = ordenadas.filter((o) => !o.fueDespachada)
  const despachadas = ordenadas.filter((o) => o.fueDespachada)

  function tarjeta(o: OrdenCompraListadoDto) {
    const codigo = codigoDeOrden(o.id)
    const abierta = abiertaId === o.id
    return (
      <li
        key={o.id}
        className={`${styles.tarjeta} ${o.fueDespachada ? styles.tarjetaDespachada : styles.tarjetaPendiente}`}
      >
        <div className={styles.encabezado}>
          <h3>Orden {codigo}</h3>
          <span className={`${styles.resultado} ${o.fueDespachada ? styles.gano : styles.enviada}`}>
            {o.fueDespachada ? 'Despachada' : 'Emitida, falta despachar'}
          </span>
        </div>

        <dl className={styles.datos}>
          <div>
            <dt>Emitida el</dt>
            <dd>{fechaLarga(o.fechaEmision)}</dd>
          </div>
          {o.fueDespachada && o.fechaDespacho && (
            <div>
              <dt>Despachada el</dt>
              <dd>{fechaLarga(o.fechaDespacho)}</dd>
            </div>
          )}
          {o.fueDespachada && o.fechaEstimadaLlegada && (
            <div>
              <dt>Llega el</dt>
              <dd>{fechaLarga(o.fechaEstimadaLlegada)}</dd>
            </div>
          )}
          {o.fueDespachada && o.observacion && (
            <div className={styles.ancho}>
              <dt>Observación</dt>
              <dd>{o.observacion}</dd>
            </div>
          )}
        </dl>

        {!o.fueDespachada && !abierta && (
          <div className={styles.acciones}>
            <button
              type="button"
              className={`${styles.accion} ${styles.accionPrincipal}`}
              aria-label={`Lo despaché: orden ${codigo}`}
              onClick={() => abrirFormulario(o.id)}
            >
              Lo despaché
            </button>
          </div>
        )}

        {!o.fueDespachada && abierta && (
          <form
            className={styles.formulario}
            noValidate
            aria-label={`Despacho de la orden ${codigo}`}
            onSubmit={(e) => void confirmarDespacho(e, o)}
          >
            <div className={styles.campo}>
              <label htmlFor={`fecha-${o.id}`}>Fecha estimada de llegada (opcional)</label>
              <input
                id={`fecha-${o.id}`}
                type="date"
                min={hoyUtc()}
                value={fecha}
                onChange={(e) => setFecha(e.target.value)}
                disabled={enviando}
              />
            </div>
            <div className={styles.campo}>
              <label htmlFor={`observacion-${o.id}`}>Observación (opcional)</label>
              <textarea
                id={`observacion-${o.id}`}
                rows={3}
                maxLength={MAX_OBSERVACION}
                value={observacion}
                onChange={(e) => setObservacion(e.target.value)}
                disabled={enviando}
              />
              <span className={styles.contador}>
                {observacion.length}/{MAX_OBSERVACION}
              </span>
            </div>
            {errorForm && (
              <p className={styles.errorApi} role="alert">
                {errorForm}
              </p>
            )}
            <div className={styles.acciones}>
              <button
                type="submit"
                className={`${styles.accion} ${styles.accionPrincipal}`}
                disabled={enviando}
                aria-busy={enviando}
              >
                {enviando ? 'Enviando…' : 'Confirmar despacho'}
              </button>
              <button type="button" className={styles.accion} onClick={cerrarFormulario} disabled={enviando}>
                Cancelar
              </button>
            </div>
          </form>
        )}
      </li>
    )
  }

  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to="/proveedores" className={styles.volver}>
        ← Volver a licitaciones
      </Link>
      <h1>Mis órdenes de compra</h1>

      {mensajeExito && (
        <p className={styles.exito} role="status">
          {mensajeExito}
        </p>
      )}

      {cargando && <p role="status">Cargando…</p>}
      {!cargando && error && <p role="alert">{error}</p>}
      {!cargando && !error && ordenes.length === 0 && <p>Todavía no tenés órdenes de compra.</p>}

      {pendientes.length > 0 && (
        <section className={styles.grupo} aria-labelledby="titulo-pendientes">
          <h2 id="titulo-pendientes" className={styles.tituloGrupo}>
            Para despachar
          </h2>
          <ul className={styles.lista}>{pendientes.map(tarjeta)}</ul>
        </section>
      )}

      {!cargando && !error && ordenes.length > 0 && pendientes.length === 0 && (
        <p className={styles.alDia}>No tenés órdenes pendientes de despachar. Estás al día.</p>
      )}

      {despachadas.length > 0 && (
        <section className={styles.grupo} aria-labelledby="titulo-despachadas">
          <h2 id="titulo-despachadas" className={styles.tituloGrupo}>
            Despachadas
          </h2>
          <ul className={styles.lista}>{despachadas.map(tarjeta)}</ul>
        </section>
      )}
    </section>
  )
}
