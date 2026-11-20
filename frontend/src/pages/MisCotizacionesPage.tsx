import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { useAuth } from '../auth/useAuth'
import {
  obtenerCalificacionProveedor,
  obtenerMisCotizaciones,
} from '../licitaciones/licitacionesService'
import type { MiCotizacionDto } from '../licitaciones/licitacionesTipos'
import { formatearPesos } from '../presupuesto/formatoPesos'
import { fechaLarga } from '../requisiciones/formatoFecha'
import styles from './MisCotizacionesPage.module.css'

/** Texto y estilo de cada resultado que informa el servicio (estadoResultado). */
const RESULTADOS: Record<string, { texto: string; clase: string }> = {
  Enviada: { texto: 'Enviada, esperando el cierre', clase: 'enviada' },
  Gano: { texto: '¡Ganaste esta licitación!', clase: 'gano' },
  NoGano: { texto: 'No ganaste esta licitación', clase: 'noGano' },
  LicitacionCerrada: { texto: 'Licitación cerrada, sin adjudicar todavía', clase: 'cerrada' },
}

/** Una cotización se puede corregir solo mientras la licitación está abierta y el plazo no venció. */
function sePuedeCorregir(c: MiCotizacionDto): boolean {
  return c.estadoLicitacion === 'Abierta' && new Date(c.fechaLimiteCotizacion).getTime() > Date.now()
}

/**
 * Portal del Proveedor: sus cotizaciones con el resultado (ganó / no ganó), su calificación y,
 * mientras la licitación sigue abierta, el acceso para corregir la oferta.
 */
export function MisCotizacionesPage() {
  const { usuario } = useAuth()
  const [cotizaciones, setCotizaciones] = useState<MiCotizacionDto[]>([])
  const [cargando, setCargando] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [calificacion, setCalificacion] = useState<number | null>(null)
  const [cantidadCalificaciones, setCantidadCalificaciones] = useState(0)
  const [intento, setIntento] = useState(0)

  useEffect(() => {
    let cancelado = false
    async function cargar() {
      try {
        const datos = await obtenerMisCotizaciones()
        if (!cancelado) setCotizaciones(datos)
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

  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to="/proveedores" className={styles.volver}>
        ← Volver a licitaciones
      </Link>
      <h1>Mis cotizaciones</h1>

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

      {!cargando && !error && cotizaciones.length === 0 && <p>Todavía no enviaste cotizaciones.</p>}

      <ul className={styles.lista}>
        {cotizaciones.map((c) => {
          const resultado = RESULTADOS[c.estadoResultado] ?? { texto: c.estadoResultado, clase: 'cerrada' }
          return (
            <li key={c.cotizacionId} className={styles.tarjeta}>
              <h2>{c.numeroRequisicion}</h2>
              <p className={`${styles.resultado} ${styles[resultado.clase]}`}>{resultado.texto}</p>
              <p>
                Total ofertado: <strong>{formatearPesos(c.costoTotal)}</strong> (flete incluido) · Plazo:{' '}
                {c.plazoEntregaDias} {c.plazoEntregaDias === 1 ? 'día' : 'días'}
              </p>
              <p className={styles.detalle}>
                Enviada el {fechaLarga(c.fechaCreacion)} · Cierre: {fechaLarga(c.fechaLimiteCotizacion)}
              </p>
              {sePuedeCorregir(c) && (
                <Link
                  to={`/proveedores/licitaciones/${c.licitacionId}?corregir=${c.cotizacionId}`}
                  className={styles.corregir}
                >
                  Corregir mi cotización
                </Link>
              )}
            </li>
          )
        })}
      </ul>
    </section>
  )
}
