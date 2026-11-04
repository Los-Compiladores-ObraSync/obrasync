import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { fechaLarga } from '../requisiciones/formatoFecha'
import { obtenerObras } from '../requisiciones/requisicionesService'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import { MAX_PENDIENTES_EN_CAMPANA } from './configuracionAvisos'
import styles from './PanelNotificaciones.module.css'
import { textoPendientes } from './textos'

interface Props {
  /** Id que usa la campana en aria-controls. */
  id: string
  /** Requisiciones pendientes de aprobar. */
  cantidad: number
  /** Las requisiciones pendientes (para elegir una desde el panel). */
  pendientes?: RequisicionResponseDto[]
  /** Se llama al tocar un enlace, para que el panel se cierre. */
  onNavegar: () => void
}

/** Panel que se despliega debajo de la campana (#49): cuántas requisiciones esperan y las más
 *  antiguas, para tocar una y ir directo a aprobarla. */
export function PanelNotificaciones({ id, cantidad, pendientes = [], onNavegar }: Props) {
  const [nombresObras, setNombresObras] = useState<Record<string, string>>({})
  const hayPendientes = pendientes.length > 0

  // Los nombres de las obras se piden solo si hay algo para mostrar. Si falla no es grave:
  // se muestra la etapa y la fecha.
  useEffect(() => {
    if (!hayPendientes) return
    let cancelado = false
    obtenerObras()
      .then((obras) => {
        if (!cancelado) setNombresObras(Object.fromEntries(obras.map((o) => [o.id, o.nombre])))
      })
      .catch(() => {})
    return () => {
      cancelado = true
    }
  }, [hayPendientes])

  // Las más antiguas primero (por fecha real, no como texto), sobre una copia.
  const masAntiguas = [...pendientes]
    .sort((a, b) => Date.parse(a.fechaCreacion) - Date.parse(b.fechaCreacion))
    .slice(0, MAX_PENDIENTES_EN_CAMPANA)

  return (
    <div id={id} className={styles.panel} role="region" aria-label="Avisos">
      {cantidad > 0 ? (
        <>
          <p className={styles.texto}>{textoPendientes(cantidad)}</p>

          {masAntiguas.length > 0 && (
            <ul className={styles.lista}>
              {masAntiguas.map((req) => {
                const obra = nombresObras[req.obraId]
                const detalle = [obra, req.etapa].filter(Boolean).join(' · ')
                return (
                  <li key={req.requisicionId}>
                    <Link
                      to={`/compras/requisiciones?req=${encodeURIComponent(req.requisicionId)}`}
                      className={styles.fila}
                      onClick={onNavegar}
                    >
                      <span className={styles.numero}>{req.numeroRequisicion}</span>
                      {detalle && <span className={styles.detalle}>{detalle}</span>}
                      <span className={styles.detalle}>Creada el {fechaLarga(req.fechaCreacion)}</span>
                    </Link>
                  </li>
                )
              })}
            </ul>
          )}

          <Link to="/compras/requisiciones" className={styles.enlace} onClick={onNavegar}>
            Ir a Aprobar requisiciones
          </Link>
        </>
      ) : (
        <p className={styles.vacio}>No tenés avisos</p>
      )}
    </div>
  )
}
