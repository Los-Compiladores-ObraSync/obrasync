import { useEffect, useId, useState } from 'react'
import { ApiError } from '../api/ApiError'
import { cantidadConUnidad } from '../requisiciones/unidades'
import { LineaDeTiempoOrden } from './LineaDeTiempoOrden'
import { ESTADO_ENTREGA_PARCIAL } from '../licitaciones/entregaParcial'
import { obtenerRecepcionesDeOrden } from './recepcionesService'
import type { ItemOrdenPendiente, RecepcionesDeOrden } from './recepcionesTipos'
import styles from './SeguimientoEntrega.module.css'

interface SeguimientoEntregaProps {
  ordenCompraId: string
  /** Código corto de la orden (se usa para nombrar el botón). */
  codigo: string
}

type Carga = { tipo: 'cargando' } | { tipo: 'lista'; datos: RecepcionesDeOrden } | { tipo: 'sinDatos' } | { tipo: 'error' }

function saldoPendiente(item: ItemOrdenPendiente): number {
  return item.cantidadPendiente ?? Math.max(item.cantidadEsperada - item.cantidadRecibida, 0)
}

/**
 * El seguimiento de la entrega de una orden despachada (#224), para Compras y para el Proveedor: qué se pidió, qué
 * llegó y qué falta, y la historia (despacho, entregas parciales, problemas con sus fotos, recepción completa).
 * Es un desplegable: recién se consulta al abrirlo, así la lista de órdenes no hace un pedido por cada una.
 */
export function SeguimientoEntrega({ ordenCompraId, codigo }: SeguimientoEntregaProps) {
  const [abierto, setAbierto] = useState(false)
  const [carga, setCarga] = useState<Carga>({ tipo: 'cargando' })
  // Cada vez que cambia "intento" se vuelve a pedir (así funciona "Reintentar").
  const [intento, setIntento] = useState(0)
  const idPanel = useId()

  useEffect(() => {
    if (!abierto) return
    let cancelado = false
    obtenerRecepcionesDeOrden(ordenCompraId)
      .then((datos) => {
        if (!cancelado) setCarga({ tipo: 'lista', datos })
      })
      .catch((err: unknown) => {
        if (cancelado) return
        // 404: Recepción todavía no recibió la orden (se entera por un evento y puede tardar unos segundos).
        setCarga(err instanceof ApiError && err.status === 404 ? { tipo: 'sinDatos' } : { tipo: 'error' })
      })
    return () => {
      cancelado = true
    }
  }, [abierto, ordenCompraId, intento])

  function alternar() {
    // Al abrir se vuelve a consultar: desde la última vez pudo llegar mercadería o registrarse un problema.
    if (!abierto) setCarga({ tipo: 'cargando' })
    setAbierto((actual) => !actual)
  }

  function reintentar() {
    setCarga({ tipo: 'cargando' })
    setIntento((n) => n + 1)
  }

  return (
    <div className={styles.contenedor}>
      <button
        type="button"
        className={styles.alternar}
        aria-expanded={abierto}
        aria-controls={idPanel}
        aria-label={`${abierto ? 'Ocultar' : 'Ver'} seguimiento de la entrega de la orden ${codigo}`}
        onClick={alternar}
      >
        {abierto ? 'Ocultar seguimiento de la entrega' : 'Ver seguimiento de la entrega'}
      </button>

      {abierto && (
        <div id={idPanel} className={styles.panel}>
          {carga.tipo === 'cargando' && <p role="status">Cargando el seguimiento…</p>}

          {carga.tipo === 'sinDatos' && (
            <p className={styles.ayuda} role="status">
              Todavía no hay datos de entrega para esta orden. Probá de nuevo en unos segundos.
            </p>
          )}

          {carga.tipo === 'error' && (
            <div>
              <p className={styles.error} role="alert">
                No se pudo cargar el seguimiento de la entrega.
              </p>
              <button type="button" className={styles.reintentar} onClick={reintentar}>
                Reintentar
              </button>
            </div>
          )}

          {carga.tipo === 'lista' && <Detalle datos={carga.datos} />}
        </div>
      )}
    </div>
  )
}

function Detalle({ datos }: { datos: RecepcionesDeOrden }) {
  const hayMovimientos = Boolean(datos.fechaDespacho) || datos.recepciones.length > 0
  return (
    <>
      {datos.tieneProblemaAbierto && (
        <p className={styles.aviso} role="status">
          {datos.esperaNuevoDespacho
            ? 'La entrega tuvo un problema y volvió al Proveedor. Hasta que la despache de nuevo, no se puede recibir.'
            : 'Hay un problema de entrega abierto en esta orden.'}
        </p>
      )}
      {datos.estadoEntrega === ESTADO_ENTREGA_PARCIAL && !datos.estaCompleta && (
        <p className={styles.aviso} role="status">
          {ESTADO_ENTREGA_PARCIAL}: llegó una parte y el Proveedor tiene que despachar el resto.
        </p>
      )}
      {datos.estaCompleta && (
        <p className={styles.completa} role="status">
          La orden se recibió por completo.
        </p>
      )}

      {datos.items.length > 0 && (
        <>
          <p className={styles.subtitulo}>Materiales</p>
          <ul className={styles.materiales} aria-label="Materiales de la orden">
            {datos.items.map((item) => (
              <li key={item.articuloId}>
                {item.descripcionArticulo}: Pedido {cantidadConUnidad(item.cantidadEsperada, item.unidadMedida)} · Recibido{' '}
                {cantidadConUnidad(item.cantidadRecibida, item.unidadMedida)} · Pendiente{' '}
                {cantidadConUnidad(saldoPendiente(item), item.unidadMedida)}
              </li>
            ))}
          </ul>
        </>
      )}

      {hayMovimientos ? (
        <>
          <p className={styles.subtitulo}>Historia de la entrega</p>
          <LineaDeTiempoOrden
            despacho={{
              fechaDespacho: datos.fechaDespacho,
              fechaEstimadaLlegada: datos.fechaEstimadaLlegada,
              observacionDespacho: datos.observacionDespacho,
            }}
            recepciones={datos.recepciones}
          />
        </>
      ) : (
        <p className={styles.ayuda}>Todavía no hay movimientos en esta orden.</p>
      )}
    </>
  )
}
