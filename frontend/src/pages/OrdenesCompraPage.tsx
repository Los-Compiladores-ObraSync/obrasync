import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { useAuth } from '../auth/useAuth'
import { ChatOrden } from '../chat/ChatOrden'
import { useChatEnLista } from '../chat/useChatEnLista'
import { CalificacionProveedor } from '../licitaciones/CalificacionProveedor'
import { listarOrdenesCompra } from '../licitaciones/licitacionesService'
import type { OrdenCompraListadoDto } from '../licitaciones/licitacionesTipos'
import { SeguimientoEntrega } from '../recepciones/SeguimientoEntrega'
import { fechaLarga } from '../requisiciones/formatoFecha'
import { obtenerObras } from '../requisiciones/requisicionesService'
import { destinoDeRegreso } from '../historialCompras/contextoNavegacion'
import { tieneEntregaParcial } from '../licitaciones/entregaParcial'
import styles from './OrdenesCompraPage.module.css'

/** Código corto para nombrar una orden en pantalla: los primeros 8 caracteres de su id. */
function codigoDeOrden(id: string): string {
  return id.slice(0, 8).toUpperCase()
}

/** Primero las que faltan despachar (lo que Compras tiene que hacer) y, dentro de cada grupo, las más nuevas. */
function ordenarParaDespachar(ordenes: OrdenCompraListadoDto[]): OrdenCompraListadoDto[] {
  return [...ordenes].sort((a, b) => {
    if (a.fueDespachada !== b.fueDespachada) return a.fueDespachada ? 1 : -1
    return new Date(b.fechaEmision).getTime() - new Date(a.fechaEmision).getTime()
  })
}

/** Órdenes de compra para Compras (#205, #223): el despacho lo marca el Proveedor (#216) y acá solo se ve.
 *  Recién después el Capataz ve la orden en "Recibir mercadería". Solo Comprador y Admin. */
export function OrdenesCompraPage() {
  const { usuario } = useAuth()
  const puedeCalificar = usuario?.rol === 'Comprador'
  const [searchParams] = useSearchParams()
  const ordenSolicitadaId = searchParams.get('ordenCompraId')
  const [ordenes, setOrdenes] = useState<OrdenCompraListadoDto[]>([])
  const [nombresObras, setNombresObras] = useState<Record<string, string>>({})
  const [cargando, setCargando] = useState(true)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)
  // Cada vez que cambia "intento" se vuelve a pedir la lista ("Reintentar").
  const [intento, setIntento] = useState(0)
  const chat = useChatEnLista()

  useEffect(() => {
    let cancelado = false

    async function cargar() {
      try {
        const [lista, obras] = await Promise.all([
          listarOrdenesCompra(),
          // Si falla la lista de obras no es grave: se muestra "Obra" sin nombre.
          obtenerObras().catch(() => []),
        ])
        if (cancelado) return
        setOrdenes(lista)
        setNombresObras(Object.fromEntries(obras.map((o) => [o.id, o.nombre])))
        setErrorCarga(null)
      } catch {
        if (!cancelado) {
          setErrorCarga('No se pudieron cargar las órdenes de compra. Intentá nuevamente.')
        }
      } finally {
        if (!cancelado) setCargando(false)
      }
    }

    void cargar()
    return () => {
      cancelado = true
    }
  }, [intento])

  function reintentar() {
    setErrorCarga(null)
    setCargando(true)
    setIntento((n) => n + 1)
  }

  const visibles = ordenarParaDespachar(ordenes)

  useEffect(() => {
    if (cargando || !ordenSolicitadaId) return
    const tarjeta = document.getElementById(`orden-${ordenSolicitadaId}`)
    if (tarjeta && typeof tarjeta.scrollIntoView === 'function') tarjeta.scrollIntoView({ block: 'center' })
  }, [cargando, ordenSolicitadaId, ordenes])

  return (
    <section className={styles.contenedor}>
      <Link to={destinoDeRegreso(searchParams)} className={styles.volverEnlace}>
        ← Volver al inicio
      </Link>
      <h1>Órdenes de compra</h1>
      <p className="intro">
        El proveedor marca cada orden como despachada cuando envía la mercadería. Recién ahí el Capataz la
        ve para recibirla en la obra. Acá podés seguir el estado de cada una.
      </p>

      {cargando && <p role="status">Cargando…</p>}

      {!cargando && errorCarga && (
        <div>
          <p className={styles.errorApi} role="alert">
            {errorCarga}
          </p>
          <button type="button" className={styles.boton} onClick={reintentar}>
            Reintentar
          </button>
        </div>
      )}

      {!cargando && !errorCarga && ordenes.length === 0 && (
        <div className={styles.tarjeta} role="status">
          <p className={styles.subtitulo}>Todavía no hay órdenes de compra.</p>
          <p className={styles.dato}>Se crean cuando adjudicás una licitación.</p>
        </div>
      )}

      {!cargando && !errorCarga && ordenSolicitadaId && !ordenes.some((orden) => orden.id === ordenSolicitadaId) && (
        <p role="alert">No encontramos la orden {ordenSolicitadaId} en la lista actual. Podés revisar todas las Órdenes de compra.</p>
      )}

      {!cargando && !errorCarga && visibles.length > 0 && (
        <ul className={styles.lista}>
          {visibles.map((orden) => {
            const codigo = codigoDeOrden(orden.id)
            const entregaParcial = tieneEntregaParcial(orden)
            return (
              <li key={orden.id} id={`orden-${orden.id}`} className={styles.orden} data-despachada={orden.fueDespachada}>
                <div className={styles.cabecera}>
                  <span className={styles.ordenObra}>{nombresObras[orden.obraId] ?? 'Obra'}</span>
                  <span className={styles.estado}>
                    {entregaParcial && !orden.recibidaCompleta ? 'Entrega parcial' : orden.fueDespachada ? 'Despachada' : 'Sin despachar'}
                  </span>
                </div>
                {orden.id === ordenSolicitadaId && <p role="status">Esta es la orden generada por la adjudicación.</p>}
                {chat.problemaDe(orden).abierto && (
                  <p className={styles.problema} role="status">
                    Entrega con problema{chat.problemaDe(orden).motivo ? `: ${chat.problemaDe(orden).motivo}` : ''}
                  </p>
                )}
                {entregaParcial && !orden.recibidaCompleta && (
                  <p className={styles.parcial} role="status">
                    {orden.puedeRedespachar
                      ? 'Entrega parcial: Recepción registró una parte del pedido. Falta que el proveedor despache el resto.'
                      : 'Entrega parcial: falta recibir el resto del pedido.'}
                  </p>
                )}
                <p className={styles.dato}>Orden {codigo}</p>
                <p className={styles.dato}>Emitida el {fechaLarga(orden.fechaEmision)}</p>
                {orden.fueDespachada && orden.fechaDespacho && (
                  <p className={styles.dato}>Despachada el {fechaLarga(orden.fechaDespacho)} por el proveedor</p>
                )}
                {orden.fueDespachada && orden.fechaEstimadaLlegada && (
                  <p className={styles.dato}>Llega el {fechaLarga(orden.fechaEstimadaLlegada)}</p>
                )}
                {orden.fueDespachada && orden.observacion && (
                  <p className={styles.dato}>Observación: {orden.observacion}</p>
                )}
                {!orden.fueDespachada && (
                  <p className={styles.dato}>Esperando que el proveedor la despache.</p>
                )}
                {orden.fueDespachada && !orden.recibidaCompleta && !entregaParcial && (
                  <p className={styles.dato}>Siguiente paso: Recepción debe registrar las entregas. <Link to="/compras/recepcion">Ir a Recepción</Link></p>
                )}
                {orden.recibidaCompleta && <p className={styles.dato}>La recepción está completa. Ya no requiere seguimiento de entrega.</p>}

                {/* Seguimiento (#224): qué llegó y qué falta, y la historia de la entrega con sus problemas y fotos. */}
                {orden.fueDespachada && <SeguimientoEntrega ordenCompraId={orden.id} codigo={codigo} />}

                {/* Chat con el proveedor (#225): en vivo, con contador de no leídos. */}
                <ChatOrden
                  ordenCompraId={orden.id}
                  codigo={codigo}
                  noLeidos={chat.noLeidosDe(orden)}
                  problema={chat.problemaDe(orden)}
                  alLeer={() => chat.marcarLeida(orden.id)}
                  alCambiarProblema={(estado) => chat.cambiarProblema(orden.id, estado)}
                />

                {puedeCalificar && <CalificacionProveedor
                  ordenCompraId={orden.id}
                  nombreProveedor={`el proveedor de la orden ${codigo}`}
                  titulo={`¿Cómo cumplió el proveedor de la orden ${codigo}?`}
                  recibidaCompleta={orden.recibidaCompleta === true}
                  calificacionActual={orden.miCalificacion}
                  comentarioActual={orden.miComentarioCalificacion}
                  alGuardar={(calificacion, comentario) =>
                    setOrdenes((actuales) => actuales.map((actual) => actual.id === orden.id
                      ? { ...actual, miCalificacion: calificacion, miComentarioCalificacion: comentario }
                      : actual))
                  }
                />}
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
