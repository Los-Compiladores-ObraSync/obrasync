import { useEffect, useId, useState } from 'react'
import { ApiError } from '../api/ApiError'
import type { OrdenCompraListadoDto } from '../licitaciones/licitacionesTipos'
import { cantidadConUnidad } from '../requisiciones/unidades'
import { LineaDeTiempoOrden } from './LineaDeTiempoOrden'
import { ESTADO_ENTREGA_PARCIAL } from '../licitaciones/entregaParcial'
import { obtenerRecepcionesDeOrden } from './recepcionesService'
import { saldoPendiente } from './saldoPendiente'
import type { RecepcionesDeOrden } from './recepcionesTipos'
import styles from './SeguimientoEntrega.module.css'

type OrdenEnSeguimiento = Pick<OrdenCompraListadoDto, 'numeroDespacho' | 'puedeRedespachar' | 'items'>

interface SeguimientoEntregaProps {
  orden?: OrdenEnSeguimiento
  ordenCompraId: string
  /** Código corto de la orden (se usa para nombrar el botón). */
  codigo: string
}

type Carga = { tipo: 'cargando' } | { tipo: 'lista'; datos: RecepcionesDeOrden } | { tipo: 'sinDatos' } | { tipo: 'error' }

/**
 * El seguimiento de la entrega de una orden despachada (#224), para Compras y para el Proveedor: qué se pidió, qué
 * llegó y qué falta, y la historia (despacho, entregas parciales, problemas con sus fotos, recepción completa).
 * Es un desplegable: recién se consulta al abrirlo, así la lista de órdenes no hace un pedido por cada una.
 */
export function SeguimientoEntrega({ ordenCompraId, codigo, orden }: SeguimientoEntregaProps) {
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

          {carga.tipo === 'lista' && <Detalle datos={carga.datos} orden={orden} />}
        </div>
      )}
    </div>
  )
}

function Detalle({ datos, orden }: { datos: RecepcionesDeOrden; orden?: OrdenEnSeguimiento }) {
  const parcial = datos.estadoEntrega === ESTADO_ENTREGA_PARCIAL && !datos.estaCompleta
  const numeroDespacho = orden?.numeroDespacho ?? 0
  const remanenteEnviado = parcial && numeroDespacho > 1 && orden?.puedeRedespachar === false
  const materiales = new Map(orden?.items?.map((item) => [item.articuloId, item]))
  const recepciones = datos.recepciones.map((recepcion) => ({
    ...recepcion,
    detalles: recepcion.detalles?.map((detalle) => ({
      ...detalle,
      descripcionArticulo: detalle.articuloId
        ? materiales.get(detalle.articuloId)?.presentacionAdjudicada?.trim() || detalle.descripcionArticulo
        : detalle.descripcionArticulo,
    })),
  }))
  const hayMovimientos = Boolean(datos.fechaDespacho) || datos.recepciones.length > 0
  return (
    <>
      {datos.tieneProblemaAbierto && (
        <p className={styles.aviso} role="status">
          Hay un problema de entrega abierto en esta orden.
        </p>
      )}
      {parcial && !datos.tieneProblemaAbierto && (
        <p className={remanenteEnviado ? styles.ayuda : styles.aviso} role="status">
          {remanenteEnviado ? (
            <><strong>Envío N.º {numeroDespacho} despachado.</strong> El capataz debe confirmar la recepción.</>
          ) : (
            <><strong>Entrega parcial.</strong> El proveedor debe despachar el resto.</>
          )}
        </p>
      )}
      {datos.estaCompleta && (
        <p className={styles.completa} role="status">
          <strong>Entrega completa.</strong> Se recibieron todos los materiales.
        </p>
      )}

      {datos.items.length > 0 && (
        <>
          <p className={styles.subtitulo}>Materiales</p>
          <ul className={styles.materiales} aria-label="Materiales de la orden">
            {datos.items.map((item) => {
              const adjudicado = materiales.get(item.articuloId)
              const presentacion = adjudicado?.presentacionAdjudicada?.trim()
              const descripcion = adjudicado?.descripcion || item.descripcionArticulo
              const sufijo = ` (${adjudicado?.presentacionAdjudicada})`
              const original = presentacion && descripcion.endsWith(sufijo)
                ? descripcion.slice(0, -sufijo.length)
                : descripcion
              const etiquetaSaldo = parcial && !remanenteEnviado && !datos.tieneProblemaAbierto
                ? 'Por despachar'
                : 'Por recibir'

              return (
                <li key={item.articuloId}>
                  <strong>{presentacion || descripcion}</strong>
                  <div>
                    Adjudicado: {cantidadConUnidad(item.cantidadEsperada, item.unidadMedida)} · Recibido:{' '}
                    {cantidadConUnidad(item.cantidadRecibida, item.unidadMedida)} ·{' '}
                    <strong>{etiquetaSaldo}: {cantidadConUnidad(saldoPendiente(item), item.unidadMedida)}</strong>
                  </div>
                  {presentacion && original && <div>Pedido original: {original}</div>}
                </li>
              )
            })}
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
            recepciones={recepciones}
          />
        </>
      ) : (
        <p className={styles.ayuda}>Todavía no hay movimientos en esta orden.</p>
      )}
    </>
  )
}
