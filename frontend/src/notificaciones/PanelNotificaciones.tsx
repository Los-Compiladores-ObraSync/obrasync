import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { fechaLarga } from '../requisiciones/formatoFecha'
import { obtenerObras } from '../requisiciones/requisicionesService'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import type { Aviso } from './avisosFlujo'
import { MAX_NOVEDADES_EN_CAMPANA, MAX_PENDIENTES_EN_CAMPANA } from './configuracionAvisos'
import { ListaAvisos } from './ListaAvisos'
import type { Novedad } from './novedades'
import styles from './PanelNotificaciones.module.css'
import { textoDeNovedad, textoNovedades, textoOrdenesBloqueadas, textoPendientes } from './textos'

interface Props {
  /** Id que usa la campana en aria-controls. */
  id: string
  /** Requisiciones pendientes de aprobar. */
  cantidadPendientes?: number
  /** Compatibilidad: si se pasa cantidad se interpreta como cantidadPendientes. */
  cantidad?: number
  /** Las requisiciones pendientes (para elegir una desde el panel). */
  pendientes?: RequisicionResponseDto[]
  /** Órdenes de compra bloqueadas por desvío presupuestario (#97). */
  cantidadBloqueadas?: number
  /** Estado de la carga compartida de órdenes bloqueadas. */
  bloqueadasCargadas?: boolean
  huboErrorBloqueadas?: boolean
  onReintentarBloqueadas?: () => void
  /** Avisos propios del Capataz (requisición aprobada o rechazada) y del Proveedor (licitación nueva) (#163). */
  novedades?: Novedad[]
  /** Marca novedades como vistas (sin argumento, todas). */
  onMarcarNovedades?: (cuales?: Novedad[]) => void
  /** Avisos del flujo de órdenes (#219) que hay que hacer: se van solos cuando se hacen. */
  avisosParaHacer?: Aviso[]
  /** Avisos del flujo de órdenes (#219) que pasaron y todavía no se vieron. */
  avisosNovedades?: Aviso[]
  /** Marca avisos del flujo como vistos (sin argumento, todos). */
  onMarcarAvisos?: (cuales?: Aviso[]) => void
  /** Se llama al tocar un enlace, para que el panel se cierre. */
  onNavegar: () => void
}

/** Panel que se despliega debajo de la campana (#49, #97): avisos de requisiciones pendientes
 *  y de órdenes de compra bloqueadas por presupuesto. */
export function PanelNotificaciones({
  id,
  cantidadPendientes,
  cantidad,
  pendientes = [],
  cantidadBloqueadas = 0,
  bloqueadasCargadas = true,
  huboErrorBloqueadas = false,
  onReintentarBloqueadas,
  novedades = [],
  onMarcarNovedades,
  avisosParaHacer = [],
  avisosNovedades = [],
  onMarcarAvisos,
  onNavegar,
}: Props) {
  const [nombresObras, setNombresObras] = useState<Record<string, string>>({})
  const totalPendientes = cantidadPendientes ?? cantidad ?? 0
  const hayPendientes = totalPendientes > 0
  const hayBloqueadas = cantidadBloqueadas > 0
  const hayNovedades = novedades.length > 0
  const hayParaHacerFlujo = avisosParaHacer.length > 0
  const hayNovedadesFlujo = avisosNovedades.length > 0
  const hayParaHacer = hayPendientes || hayBloqueadas || hayParaHacerFlujo
  const hayAlgoNuevo = hayNovedades || hayNovedadesFlujo
  const hayAvisos = hayParaHacer || hayAlgoNuevo || huboErrorBloqueadas || !bloqueadasCargadas

  // Los nombres de las obras se piden solo si hay algo para mostrar en la lista de requisiciones.
  useEffect(() => {
    if (pendientes.length === 0) return
    let cancelado = false
    obtenerObras()
      .then((obras) => {
        if (!cancelado) setNombresObras(Object.fromEntries(obras.map((o) => [o.id, o.nombre])))
      })
      .catch(() => {})
    return () => {
      cancelado = true
    }
  }, [pendientes.length])

  // Las más antiguas primero (por fecha real, no como texto), sobre una copia.
  const masAntiguas = [...pendientes]
    .sort((a, b) => Date.parse(a.fechaCreacion) - Date.parse(b.fechaCreacion))
    .slice(0, MAX_PENDIENTES_EN_CAMPANA)

  return (
    <div id={id} className={styles.panel} role="region" aria-label="Avisos">
      {hayAvisos ? (
        <>
          {huboErrorBloqueadas && (
            <div role="alert" className={styles.seccionAviso}>
              <p className={styles.texto}>
                {bloqueadasCargadas
                  ? 'No se pudieron actualizar los avisos de órdenes bloqueadas.'
                  : 'No se pudieron cargar los avisos de órdenes bloqueadas.'}
              </p>
              {onReintentarBloqueadas && (
                <button type="button" className={styles.enlace} onClick={onReintentarBloqueadas}>
                  Reintentar
                </button>
              )}
            </div>
          )}
          {!huboErrorBloqueadas && !bloqueadasCargadas && (
            <p role="status" className={styles.vacio}>Cargando avisos de órdenes bloqueadas…</p>
          )}
          {hayParaHacer && <h3 className={styles.tituloSeccion}>Para hacer</h3>}

          {hayPendientes && (
            <div className={styles.seccionAviso}>
              <p className={styles.texto}>{textoPendientes(totalPendientes)}</p>

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
            </div>
          )}

          {hayBloqueadas && (
            <div className={hayPendientes ? `${styles.seccionAviso} ${styles.seccionSeparada}` : styles.seccionAviso}>
              <p className={styles.texto}>{textoOrdenesBloqueadas(cantidadBloqueadas)}</p>
              <Link to="/compras/presupuesto" className={styles.enlace} onClick={onNavegar}>
                Ir a Presupuesto
              </Link>
            </div>
          )}

          {hayParaHacerFlujo && (
            <div className={hayPendientes || hayBloqueadas ? `${styles.seccionAviso} ${styles.seccionSeparada}` : styles.seccionAviso}>
              <ListaAvisos avisos={avisosParaHacer} onNavegar={onNavegar} />
            </div>
          )}

          {hayAlgoNuevo && (
            <h3 className={hayParaHacer ? `${styles.tituloSeccion} ${styles.seccionSeparada}` : styles.tituloSeccion}>
              Novedades
            </h3>
          )}

          {hayNovedades && (
            <div className={styles.seccionAviso}>
              <p className={styles.texto}>{textoNovedades(novedades)}</p>
              <ul className={styles.lista}>
                {novedades.slice(0, MAX_NOVEDADES_EN_CAMPANA).map((novedad) => (
                  <li key={novedad.id}>
                    <Link
                      to={novedad.ruta}
                      className={styles.fila}
                      onClick={() => {
                        onMarcarNovedades?.([novedad])
                        onNavegar()
                      }}
                    >
                      <span className={styles.numero}>{textoDeNovedad(novedad)}</span>
                      {novedad.detalle && <span className={styles.detalle}>Motivo: {novedad.detalle}</span>}
                    </Link>
                  </li>
                ))}
              </ul>
              {novedades.length > MAX_NOVEDADES_EN_CAMPANA && (
                <p className={styles.vacio}>
                  y {novedades.length - MAX_NOVEDADES_EN_CAMPANA} más
                </p>
              )}
            </div>
          )}

          {hayNovedadesFlujo && (
            <div className={styles.seccionAviso}>
              <ListaAvisos avisos={avisosNovedades} onNavegar={onNavegar} onAbrir={(aviso) => onMarcarAvisos?.([aviso])} />
            </div>
          )}

          {hayAlgoNuevo && (onMarcarNovedades || onMarcarAvisos) && (
            <button
              type="button"
              className={styles.enlace}
              onClick={() => {
                onMarcarNovedades?.()
                onMarcarAvisos?.()
              }}
            >
              Marcar como vistas
            </button>
          )}
        </>
      ) : (
        <p className={styles.vacio}>No tenés avisos</p>
      )}
    </div>
  )
}
