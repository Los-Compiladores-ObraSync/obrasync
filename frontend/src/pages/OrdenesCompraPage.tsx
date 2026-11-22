import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { ChatOrden } from '../chat/ChatOrden'
import { useChatEnLista } from '../chat/useChatEnLista'
import { CalificacionProveedor } from '../licitaciones/CalificacionProveedor'
import { listarOrdenesCompra } from '../licitaciones/licitacionesService'
import type { OrdenCompraListadoDto } from '../licitaciones/licitacionesTipos'
import { SeguimientoEntrega } from '../recepciones/SeguimientoEntrega'
import { fechaLarga } from '../requisiciones/formatoFecha'
import { obtenerObras } from '../requisiciones/requisicionesService'
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

  return (
    <section className={styles.contenedor}>
      <Link to="/" className={styles.volverEnlace}>
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

      {!cargando && !errorCarga && visibles.length > 0 && (
        <ul className={styles.lista}>
          {visibles.map((orden) => {
            const codigo = codigoDeOrden(orden.id)
            return (
              <li key={orden.id} className={styles.orden} data-despachada={orden.fueDespachada}>
                <div className={styles.cabecera}>
                  <span className={styles.ordenObra}>{nombresObras[orden.obraId] ?? 'Obra'}</span>
                  <span className={styles.estado}>
                    {orden.fueDespachada ? 'Despachada' : 'Sin despachar'}
                  </span>
                </div>
                {chat.problemaDe(orden).abierto && (
                  <p className={styles.problema} role="status">
                    Entrega con problema{chat.problemaDe(orden).motivo ? `: ${chat.problemaDe(orden).motivo}` : ''}
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

                {orden.fueDespachada && (
                  <CalificacionProveedor
                    proveedorId={orden.proveedorId}
                    nombreProveedor={`el proveedor de la orden ${codigo}`}
                    titulo={`¿Cómo cumplió el proveedor de la orden ${codigo}?`}
                    alGuardar={() => undefined}
                    claveRecuerdo={`obrasync.calificacion.orden.${orden.id}`}
                  />
                )}
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
