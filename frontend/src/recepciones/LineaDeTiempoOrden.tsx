import { fechaLarga } from '../requisiciones/formatoFecha'
import { cantidadConUnidad } from '../requisiciones/unidades'
import { FotoEvidencia } from './FotoEvidencia'
import { fechaDeRecepcion } from './fechasRecepcion'
import type { RecepcionHistorial } from './recepcionesTipos'
import styles from './LineaDeTiempoOrden.module.css'

/** Lo que informó el Proveedor al despachar la orden (#216). */
export interface DespachoOrden {
  fechaDespacho?: string | null
  fechaEstimadaLlegada?: string | null
  observacionDespacho?: string | null
}

interface LineaDeTiempoOrdenProps {
  despacho?: DespachoOrden
  /** Las recepciones de la orden: parciales, completas y entregas con problema (estado 4). */
  recepciones: RecepcionHistorial[]
}

/** Orden en que se registraron: la más vieja primero. */
function ordenCronologico(recepciones: RecepcionHistorial[]): RecepcionHistorial[] {
  const clave = (r: RecepcionHistorial) => r.createdAt ?? r.fechaRecepcion ?? ''
  return [...recepciones].sort((a, b) => clave(a).localeCompare(clave(b)))
}

function textoDeEstado(estado?: number): string {
  if (estado === 2) return 'Parcial'
  if (estado === 3) return 'Completa'
  if (estado === undefined) return 'Estado no disponible'
  return `Estado ${estado}`
}

/**
 * La historia de una orden de compra (#224): cuándo la despachó el Proveedor y para cuándo la estima, cada recepción
 * parcial, cada entrega con problema (con sus fotos) y la recepción completa. La usan el Capataz, Compras y el Proveedor.
 */
export function LineaDeTiempoOrden({ despacho, recepciones }: LineaDeTiempoOrdenProps) {
  const hayDespacho = Boolean(despacho?.fechaDespacho)
  if (!hayDespacho && recepciones.length === 0) return null

  return (
    <ol className={styles.linea} aria-label="Línea de tiempo de la orden">
      {hayDespacho && despacho && (
        <li className={styles.hito}>
          <strong>Despachada por el Proveedor</strong> · {fechaLarga(despacho.fechaDespacho)}
          {despacho.fechaEstimadaLlegada && <p className={styles.detalle}>Llega el {fechaLarga(despacho.fechaEstimadaLlegada)}.</p>}
          {despacho.observacionDespacho && <p className={styles.detalle}>Observación del Proveedor: {despacho.observacionDespacho}</p>}
        </li>
      )}

      {ordenCronologico(recepciones).map((recepcion, indice) => {
        // Estado 4 = Rechazada: es una entrega con problema, no suma nada a lo recibido.
        if (recepcion.estado === 4) {
          const fotos = recepcion.fotos ?? []
          return (
            <li key={recepcion.id ?? indice} className={`${styles.hito} ${styles.hitoProblema}`}>
              <strong>Entrega con problema</strong> · {fechaDeRecepcion(recepcion.fechaRecepcion)}
              {recepcion.motivoProblema && <p className={styles.detalle}>Motivo: {recepcion.motivoProblema}</p>}
              {recepcion.observaciones && <p className={styles.detalle}>Detalle: {recepcion.observaciones}</p>}
              {recepcion.id && fotos.length > 0 && (
                <div className={styles.fotos}>
                  {fotos.map((foto, posicion) => (
                    <FotoEvidencia key={foto.id} recepcionId={recepcion.id as string} foto={foto} numero={posicion + 1} />
                  ))}
                </div>
              )}
            </li>
          )
        }

        return (
          <li key={recepcion.id ?? indice} className={styles.hito}>
            <strong>Remito {recepcion.numeroRemito ?? 'sin número'}</strong> · {fechaDeRecepcion(recepcion.fechaRecepcion)} · {textoDeEstado(recepcion.estado)}
            {(recepcion.detalles ?? []).length > 0 && (
              <ul className={styles.materiales}>
                {recepcion.detalles?.map((detalle, posicion) => (
                  <li key={detalle.id ?? detalle.articuloId ?? posicion}>
                    {detalle.descripcionArticulo ?? 'Artículo'}: {cantidadConUnidad(detalle.cantidadRecibida ?? 0, detalle.unidadMedida ?? '')}
                    {detalle.conforme === false && <strong> · Con problemas{detalle.observacion ? `: ${detalle.observacion}` : ''}</strong>}
                  </li>
                ))}
              </ul>
            )}
          </li>
        )
      })}
    </ol>
  )
}
