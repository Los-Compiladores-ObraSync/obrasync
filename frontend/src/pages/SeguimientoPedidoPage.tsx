import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router'
import { useAuth } from '../auth/useAuth'
import { obtenerHistorialRecepciones } from '../recepciones/recepcionesService'
import { obtenerRequisicion } from '../requisiciones/requisicionesService'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import { armarPasos, type EstadoPaso } from '../seguimiento/pasosSeguimiento'
import { obtenerSeguimientoLicitacion, type SeguimientoRequisicion } from '../seguimiento/seguimientoService'
import styles from './SeguimientoPedidoPage.module.css'

const TEXTO_ESTADO: Record<EstadoPaso, string> = {
  hecho: 'Listo',
  actual: 'En curso',
  pendiente: 'Pendiente',
  problema: 'Con problema',
}

interface Datos {
  requisicion: RequisicionResponseDto
  seguimiento: SeguimientoRequisicion | null
  recepciones: { estado?: number }[]
}

/**
 * "¿Qué pasó con mi pedido?": línea de tiempo de punta a punta (aprobación, cotizaciones, orden de compra,
 * despacho y recepción). Junta tres servicios, y cada uno aporta solo lo suyo.
 */
export function SeguimientoPedidoPage() {
  const { id = '' } = useParams()
  const { usuario } = useAuth()
  const [datos, setDatos] = useState<Datos | null>(null)
  const [cargando, setCargando] = useState(true)
  const [error, setError] = useState<'noExiste' | 'falla' | null>(null)
  const [intento, setIntento] = useState(0)

  useEffect(() => {
    let cancelado = false
    async function cargar() {
      setCargando(true)
      setError(null)
      try {
        // Primero la requisición: ahí se verifica que el pedido sea del Capataz (404 si no).
        const requisicion = await obtenerRequisicion(id)
        let seguimiento: SeguimientoRequisicion | null = null
        let recepciones: { estado?: number }[] = []
        if (requisicion.estado === 'Aprobada') {
          seguimiento = await obtenerSeguimientoLicitacion(id)
          const listas = await Promise.all(
            seguimiento.ordenesCompra.map((o) => obtenerHistorialRecepciones(o.ordenCompraId)),
          )
          recepciones = listas.flat()
        }
        if (!cancelado) setDatos({ requisicion, seguimiento, recepciones })
      } catch (err) {
        if (!cancelado) {
          const noExiste = typeof err === 'object' && err !== null && 'status' in err && (err as { status: number }).status === 404
          setError(noExiste ? 'noExiste' : 'falla')
        }
      } finally {
        if (!cancelado) setCargando(false)
      }
    }
    void cargar()
    return () => {
      cancelado = true
    }
  }, [id, intento])

  const esAjena =
    datos !== null && usuario?.rol === 'Capataz' && datos.requisicion.solicitanteId !== usuario.id
  const noExiste = error === 'noExiste' || esAjena

  return (
    <section className={styles.contenedor}>
      <Link to={`/obra/requisiciones/${encodeURIComponent(id)}`} className={styles.volver}>
        ← Volver al pedido
      </Link>

      {cargando && <p role="status">Cargando…</p>}
      {!cargando && noExiste && <p className={styles.error} role="alert">No encontramos ese pedido.</p>}
      {!cargando && !noExiste && error === 'falla' && (
        <div>
          <p className={styles.error} role="alert">No se pudo cargar el seguimiento. Intentá nuevamente.</p>
          <button type="button" onClick={() => setIntento((n) => n + 1)}>Reintentar</button>
        </div>
      )}

      {!cargando && !noExiste && !error && datos && (
        <>
          <h1>Seguimiento de {datos.requisicion.numeroRequisicion}</h1>
          <ol className={styles.pasos} aria-label="Pasos del pedido">
            {armarPasos(datos.requisicion, datos.seguimiento, datos.recepciones).map((paso) => (
              <li key={paso.clave} className={`${styles.paso} ${styles[paso.estado]}`}>
                <span className={styles.titulo}>
                  {paso.titulo}
                  <span className={styles.visualmenteOculto}> — {TEXTO_ESTADO[paso.estado]}</span>
                </span>
                {paso.detalle && <span className={styles.detalle}>{paso.detalle}</span>}
              </li>
            ))}
          </ol>
        </>
      )}
    </section>
  )
}
