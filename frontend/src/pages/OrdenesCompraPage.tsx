import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { ApiError } from '../api/ApiError'
import { despacharOrdenCompra, listarOrdenesCompra } from '../licitaciones/licitacionesService'
import type { OrdenCompraListadoDto } from '../licitaciones/licitacionesTipos'
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

function mensajeDeErrorDespacho(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 403) return 'No tenés permiso para despachar órdenes de compra.'
    if (err.status === 404) {
      return err.detail ?? 'La orden de compra ya no existe. Se actualizó la lista.'
    }
    if (err.status === 409) {
      return err.detail ?? 'Esa orden de compra ya estaba despachada. Se actualizó la lista.'
    }
  }
  return 'No se pudo conectar con el servidor. Intentá nuevamente.'
}

/** Despacho de órdenes de compra (#205): Compras marca cuándo el proveedor envió la mercadería.
 *  Recién después el Capataz ve la orden en "Recibir mercadería". Solo Comprador y Admin. */
export function OrdenesCompraPage() {
  const [ordenes, setOrdenes] = useState<OrdenCompraListadoDto[]>([])
  const [nombresObras, setNombresObras] = useState<Record<string, string>>({})
  const [cargando, setCargando] = useState(true)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)
  // Cada vez que cambia "intento" se vuelve a pedir la lista ("Reintentar" y refresco tras un 404/409).
  const [intento, setIntento] = useState(0)

  // Orden que está pidiendo confirmación y orden cuyo pedido está en curso.
  const [confirmandoId, setConfirmandoId] = useState<string | null>(null)
  const [despachandoId, setDespachandoId] = useState<string | null>(null)
  const [mensajeExito, setMensajeExito] = useState<string | null>(null)
  const [errorAccion, setErrorAccion] = useState<string | null>(null)
  // Guard inmediato contra el doble toque: el estado recién cambia en el próximo render y un
  // segundo toque podría colarse antes (mismo criterio que la recepción y la adjudicación).
  const enVueloRef = useRef(false)

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

  function pedirConfirmacion(id: string) {
    if (enVueloRef.current) return
    setMensajeExito(null)
    setErrorAccion(null)
    setConfirmandoId(id)
  }

  async function confirmarDespacho(orden: OrdenCompraListadoDto) {
    if (enVueloRef.current) return
    enVueloRef.current = true
    setDespachandoId(orden.id)
    setErrorAccion(null)
    setMensajeExito(null)

    try {
      const actualizada = await despacharOrdenCompra(orden.id)
      setOrdenes((actuales) => actuales.map((o) => (o.id === orden.id ? { ...o, ...actualizada } : o)))
      setMensajeExito(
        `La orden ${codigoDeOrden(orden.id)} quedó despachada. Se avisó a Recepción para que el Capataz pueda recibirla.`,
      )
    } catch (err) {
      setErrorAccion(mensajeDeErrorDespacho(err))
      // Si la orden ya no existe o ya estaba despachada, la lista en pantalla quedó vieja: se actualiza.
      if (err instanceof ApiError && (err.status === 404 || err.status === 409)) {
        setIntento((n) => n + 1)
      }
    } finally {
      enVueloRef.current = false
      setDespachandoId(null)
      setConfirmandoId(null)
    }
  }

  const visibles = ordenarParaDespachar(ordenes)

  return (
    <section className={styles.contenedor}>
      <Link to="/compras" className={styles.volverEnlace}>
        ← Volver a Gestión y Compras
      </Link>
      <h1>Órdenes de compra</h1>
      <p className="intro">
        Marcá una orden como despachada cuando el proveedor la envía. Recién ahí el Capataz la ve
        para recibirla en la obra.
      </p>

      {mensajeExito && (
        <p className={styles.exito} role="status">
          {mensajeExito}
        </p>
      )}
      {errorAccion && (
        <p className={styles.errorApi} role="alert">
          {errorAccion}
        </p>
      )}

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
            const confirmando = confirmandoId === orden.id
            const despachando = despachandoId === orden.id
            return (
              <li key={orden.id} className={styles.orden} data-despachada={orden.fueDespachada}>
                <div className={styles.cabecera}>
                  <span className={styles.ordenObra}>{nombresObras[orden.obraId] ?? 'Obra'}</span>
                  <span className={styles.estado}>
                    {orden.fueDespachada ? 'Despachada' : 'Sin despachar'}
                  </span>
                </div>
                <p className={styles.dato}>Orden {codigo}</p>
                <p className={styles.dato}>Emitida el {fechaLarga(orden.fechaEmision)}</p>
                {orden.fueDespachada && orden.fechaDespacho && (
                  <p className={styles.dato}>Despachada el {fechaLarga(orden.fechaDespacho)}</p>
                )}

                {!orden.fueDespachada && !confirmando && (
                  <button
                    type="button"
                    className={styles.boton}
                    aria-label={`Marcar como despachada la orden ${codigo}`}
                    onClick={() => pedirConfirmacion(orden.id)}
                    disabled={despachandoId !== null}
                  >
                    Marcar como despachada
                  </button>
                )}

                {!orden.fueDespachada && confirmando && (
                  <div
                    className={styles.confirmar}
                    role="group"
                    aria-label={`Confirmar despacho de la orden ${codigo}`}
                  >
                    <p className={styles.pregunta}>
                      ¿Marcar la orden {codigo} como despachada? No se puede deshacer.
                    </p>
                    <button
                      type="button"
                      className={styles.boton}
                      onClick={() => void confirmarDespacho(orden)}
                      disabled={despachando}
                      aria-busy={despachando}
                    >
                      {despachando ? 'Despachando…' : 'Sí, marcar como despachada'}
                    </button>
                    <button
                      type="button"
                      className={styles.botonSecundario}
                      onClick={() => setConfirmandoId(null)}
                      disabled={despachando}
                    >
                      Cancelar
                    </button>
                  </div>
                )}
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
