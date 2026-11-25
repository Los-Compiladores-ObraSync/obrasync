import { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { ApiError } from '../api/ApiError'
import { traerTodas } from '../api/paginacion'
import { BotonVerMas } from '../componentes/BotonVerMas'
import { obtenerCapataces } from '../obras/capatacesService'
import {
  aprobarRequisicion,
  listarRequisiciones,
  obtenerInsumosCatalogo,
  obtenerObras,
  rechazarRequisicion,
} from '../requisiciones/requisicionesService'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import { usePendientes } from '../notificaciones/usePendientes'
import { PresupuestoDeEtapa } from '../presupuesto/PresupuestoDeEtapa'
import { cantidadConUnidad } from '../requisiciones/unidades'
import styles from './AprobarRequisicionesPage.module.css'

/** Motivos más comunes: se tocan en vez de escribirlos de cero. Después se puede agregar detalle al texto. */
const MOTIVOS_RECHAZO = [
  'Falta información en la requisición',
  'Cantidad mayor a la necesaria',
  'Material equivocado para la etapa',
  'Supera el presupuesto de la etapa',
  'Ya existe otra requisición igual',
] as const

function formatearPesos(valor: number): string {
  return `$ ${valor.toLocaleString('es-AR', { maximumFractionDigits: 2 })}`
}

function mensajeDeErrorAprobar(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 403) return 'No tenés permiso para aprobar requisiciones.'
    if (err.status === 404) return err.detail ?? 'La requisición ya no existe. Recargá la lista.'
    if (err.status === 409) {
      return err.detail ?? 'La requisición ya no está pendiente. Recargá la lista.'
    }
    if (err.status === 400) return err.detail ?? 'No se pudo aprobar la requisición.'
  }
  return 'No se pudo conectar con el servidor. Intentá nuevamente.'
}

function mensajeDeErrorRechazar(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 403) return 'No tenés permiso para rechazar requisiciones.'
    if (err.status === 404) return err.detail ?? 'La requisición ya no existe. Recargá la lista.'
    if (err.status === 409) {
      return err.detail ?? 'La requisición ya no está pendiente. Recargá la lista.'
    }
    if (err.status === 400) return err.detail ?? 'No se pudo rechazar la requisición.'
  }
  return 'No se pudo conectar con el servidor. Intentá nuevamente.'
}

export function AprobarRequisicionesPage() {
  const [pendientes, setPendientes] = useState<RequisicionResponseDto[]>([])
  const [nombresObras, setNombresObras] = useState<Record<string, string>>({})
  // Nombre del solicitante por id (#238): el contrato de requisiciones solo trae el id.
  const [nombresSolicitantes, setNombresSolicitantes] = useState<Record<string, string>>({})
  const [cargando, setCargando] = useState(true)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)

  // Paginación (#172): el servicio entrega de a 50; "Ver más" pide la página siguiente.
  const [total, setTotal] = useState<number | undefined>(undefined)
  const [paginaCargada, setPaginaCargada] = useState(1)
  const [cargandoMas, setCargandoMas] = useState(false)
  const [errorMas, setErrorMas] = useState(false)
  const [operacionEnVuelo, setOperacionEnVuelo] = useState<{
    tipo: 'aprobar' | 'rechazar'
    id: string
  } | null>(null)
  const [errorAccion, setErrorAccion] = useState<string | null>(null)
  const [mensajeEstado, setMensajeEstado] = useState<string | null>(null)
  // La última requisición aprobada desde esta pantalla: se le ofrece el paso siguiente (publicar la licitación).
  const [aprobadaReciente, setAprobadaReciente] = useState<{ id: string; numero: string } | null>(null)
  const [rechazandoId, setRechazandoId] = useState<string | null>(null)
  const [motivo, setMotivo] = useState('')
  const [errorMotivo, setErrorMotivo] = useState<string | null>(null)
  // Precio de referencia por artículo (del catálogo): sirve para estimar el costo al decidir.
  const [preciosReferencia, setPreciosReferencia] = useState<Record<string, number>>({})
  const { refrescar: refrescarCampana } = usePendientes()

  // Si se llega desde la campana (?req=<id>), esa requisición va primera y queda marcada.
  const [params] = useSearchParams()
  const seleccionadaId = params.get('req')

  // Guard inmediato contra el doble clic: "operacionEnVuelo" (estado) recién cambia en el próximo render.
  // Un único ref para proteger tanto aprobar como rechazar.
  const peticionEnVueloRef = useRef(false)

  // Cada vez que cambia "intento" se vuelve a pedir la lista (así funciona "Reintentar")
  const [intento, setIntento] = useState(0)

  useEffect(() => {
    let cancelado = false

    async function cargar() {
      try {
        const [pagina, obras, capataces] = await Promise.all([
          listarRequisiciones({ estado: 'Pendiente' }),
          // Si falla la lista de obras no es grave: se muestra el identificador.
          obtenerObras().catch(() => []),
          // Tampoco es grave si falla la de capataces: el detalle se muestra sin el solicitante.
          obtenerCapataces().catch(() => []),
        ])
        if (cancelado) return
        setPendientes(pagina.items)
        setTotal(pagina.total)
        setPaginaCargada(1)
        setNombresObras(Object.fromEntries(obras.map((o) => [o.id, o.nombre])))
        setNombresSolicitantes(Object.fromEntries(capataces.map((c) => [c.id, c.nombreCompleto])))
      } catch {
        if (!cancelado) {
          setErrorCarga('No se pudieron cargar las requisiciones. Intentá nuevamente.')
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

  // Los precios son un dato de apoyo: si el catálogo no responde, el detalle se muestra sin ellos.
  useEffect(() => {
    let cancelado = false
    traerTodas((pagina) => obtenerInsumosCatalogo(undefined, false, pagina))
      .then((insumos) => {
        if (cancelado) return
        const precios: Record<string, number> = {}
        for (const i of insumos) {
          if (typeof i.precioReferencia === 'number') precios[i.articuloId] = i.precioReferencia
        }
        setPreciosReferencia(precios)
      })
      .catch(() => undefined)
    return () => {
      cancelado = true
    }
  }, [])

  function reintentar() {
    setErrorCarga(null)
    setCargando(true)
    setIntento((n) => n + 1)
  }

  async function verMas() {
    if (cargandoMas) return
    setCargandoMas(true)
    setErrorMas(false)
    try {
      const pagina = await listarRequisiciones({ estado: 'Pendiente' }, paginaCargada + 1)
      // Las aprobadas o rechazadas desde acá corren la lista: una fila puede repetirse y se descarta.
      setPendientes((actuales) => {
        const ids = new Set(actuales.map((r) => r.requisicionId))
        return [...actuales, ...pagina.items.filter((r) => !ids.has(r.requisicionId))]
      })
      setTotal(pagina.total)
      setPaginaCargada((n) => n + 1)
    } catch {
      setErrorMas(true)
    } finally {
      setCargandoMas(false)
    }
  }

  async function aprobar(req: RequisicionResponseDto) {
    if (peticionEnVueloRef.current) return
    peticionEnVueloRef.current = true
    setOperacionEnVuelo({ tipo: 'aprobar', id: req.requisicionId })
    setErrorAccion(null)
    setMensajeEstado(null)
    setAprobadaReciente(null)
    try {
      const resultado = await aprobarRequisicion(req.requisicionId)
      setPendientes((actuales) => actuales.filter((r) => r.requisicionId !== req.requisicionId))
      setTotal((t) => (t === undefined ? t : Math.max(0, t - 1)))
      setMensajeEstado(`La requisición ${resultado.numeroRequisicion} fue aprobada.`)
      setAprobadaReciente({ id: req.requisicionId, numero: resultado.numeroRequisicion })
      // La campana del encabezado tiene que bajar el número enseguida.
      refrescarCampana()
    } catch (err) {
      setErrorAccion(mensajeDeErrorAprobar(err))
    } finally {
      peticionEnVueloRef.current = false
      setOperacionEnVuelo(null)
    }
  }

  function iniciarRechazo(id: string) {
    setRechazandoId(id)
    setMotivo('')
    setErrorMotivo(null)
    setErrorAccion(null)
    setMensajeEstado(null)
  }

  function cancelarRechazo() {
    setRechazandoId(null)
    setMotivo('')
    setErrorMotivo(null)
  }

  async function confirmarRechazo(req: RequisicionResponseDto) {
    const motivoLimpio = motivo.trim()
    if (!motivoLimpio) {
      setErrorMotivo('El motivo de rechazo es obligatorio.')
      return
    }

    if (peticionEnVueloRef.current) return
    peticionEnVueloRef.current = true
    setOperacionEnVuelo({ tipo: 'rechazar', id: req.requisicionId })
    setErrorAccion(null)
    setMensajeEstado(null)
    setAprobadaReciente(null)

    try {
      const resultado = await rechazarRequisicion(req.requisicionId, motivoLimpio)
      setPendientes((actuales) => actuales.filter((r) => r.requisicionId !== req.requisicionId))
      setTotal((t) => (t === undefined ? t : Math.max(0, t - 1)))
      setMensajeEstado(`La requisición ${resultado.numeroRequisicion} fue rechazada.`)
      refrescarCampana()
      setRechazandoId(null)
      setMotivo('')
      setErrorMotivo(null)
    } catch (err) {
      setErrorAccion(mensajeDeErrorRechazar(err))
      if (err instanceof ApiError && (err.status === 404 || err.status === 409)) {
        setRechazandoId(null)
        setMotivo('')
        setErrorMotivo(null)
        setIntento((n) => n + 1)
      }
    } finally {
      peticionEnVueloRef.current = false
      setOperacionEnVuelo(null)
    }
  }

  const hayPeticionEnVuelo = operacionEnVuelo !== null

  // El sort es estable: el resto conserva su orden. Si el id no está (ya se aprobó), no pasa nada.
  const visibles = seleccionadaId
    ? [...pendientes].sort(
        (a, b) =>
          Number(b.requisicionId === seleccionadaId) - Number(a.requisicionId === seleccionadaId),
      )
    : pendientes

  return (
    <section className={`${styles.contenedor} pantalla-ancha`}>
      <Link to="/" className={styles.volver}>
        ← Volver al inicio
      </Link>
      <h1>Aprobar requisiciones</h1>

      {mensajeEstado && (
        <p className={styles.exito} role="status">
          {mensajeEstado}
        </p>
      )}
      {aprobadaReciente && (
        <div className={styles.siguientePaso}>
          <p className={styles.siguientePasoTexto}>
            Siguiente paso: publicá la licitación para que los proveedores puedan cotizar.
          </p>
          <Link
            to={`/compras/requisiciones/historial/${aprobadaReciente.id}`}
            className={styles.siguientePasoBoton}
          >
            Publicar licitación de {aprobadaReciente.numero}
          </Link>
        </div>
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
          <button type="button" onClick={reintentar}>
            Reintentar
          </button>
        </div>
      )}

      {!cargando && !errorCarga && pendientes.length === 0 && (
        <p>No hay requisiciones pendientes de aprobación.</p>
      )}

      {!cargando && !errorCarga && pendientes.length > 0 && (
        <ul className={styles.lista}>
          {visibles.map((req) => (
            <li
              key={req.requisicionId}
              className={
                req.requisicionId === seleccionadaId
                  ? `${styles.tarjeta} ${styles.seleccionada}`
                  : styles.tarjeta
              }
            >
              {req.requisicionId === seleccionadaId && (
                <span className={styles.etiquetaSeleccion}>Seleccionada</span>
              )}
              <h2 className={styles.numero}>{req.numeroRequisicion}</h2>
              <p className={styles.dato}>
                Obra: {nombresObras[req.obraId] ?? req.obraId}
                {req.etapa ? ` · Etapa: ${req.etapa}` : ''}
              </p>
              {req.observaciones && (
                <p className={`${styles.dato} ${styles.observaciones}`}>
                  Observaciones: {req.observaciones}
                </p>
              )}
              <ul className={styles.items}>
                {req.items.map((it) => (
                  <li key={it.articuloId}>
                    {cantidadConUnidad(it.cantidad, it.unidadMedida)} · {it.descripcion}
                  </li>
                ))}
              </ul>

              <PresupuestoDeEtapa
                obraId={req.obraId}
                etapa={req.etapa}
                estimado={
                  req.items.some((it) => preciosReferencia[it.articuloId] !== undefined)
                    ? req.items.reduce((suma, it) => suma + (preciosReferencia[it.articuloId] ?? 0) * it.cantidad, 0)
                    : undefined
                }
              />

              <details className={styles.detalle}>
                <summary className={styles.resumenDetalle}>
                  Ver detalle completo
                </summary>
                {nombresSolicitantes[req.solicitanteId] && (
                  <p className={styles.dato}>Solicitante: {nombresSolicitantes[req.solicitanteId]}</p>
                )}
                <p className={styles.dato}>
                  Creada el {new Date(req.fechaCreacion).toLocaleDateString('es-AR')}
                </p>
                <table className={styles.tablaDetalle}>
                  <thead>
                    <tr>
                      <th scope="col">Material</th>
                      <th scope="col">Cantidad</th>
                      <th scope="col">Precio ref.</th>
                      <th scope="col">Subtotal</th>
                    </tr>
                  </thead>
                  <tbody>
                    {req.items.map((it) => {
                      const precio = preciosReferencia[it.articuloId]
                      return (
                        <tr key={it.articuloId}>
                          <td>{it.descripcion}</td>
                          <td>{cantidadConUnidad(it.cantidad, it.unidadMedida)}</td>
                          <td>{precio === undefined ? '—' : formatearPesos(precio)}</td>
                          <td>{precio === undefined ? '—' : formatearPesos(precio * it.cantidad)}</td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
                {req.items.some((it) => preciosReferencia[it.articuloId] !== undefined) && (
                  <p className={styles.totalEstimado}>
                    Total estimado:{' '}
                    {formatearPesos(
                      req.items.reduce(
                        (suma, it) => suma + (preciosReferencia[it.articuloId] ?? 0) * it.cantidad,
                        0,
                      ),
                    )}
                    {req.items.some((it) => preciosReferencia[it.articuloId] === undefined) &&
                      ' (sin contar los materiales sin precio de referencia)'}
                  </p>
                )}
                <p className={styles.aclaracion}>
                  Son precios de referencia del catálogo; el precio real sale de las cotizaciones.
                </p>
              </details>

              {rechazandoId === req.requisicionId ? (
                <div className={styles.contenedorRechazo}>
                  <label htmlFor={`motivo-${req.requisicionId}`} className={styles.labelMotivo}>
                    Motivo del rechazo
                  </label>
                  <div className={styles.motivos} role="group" aria-label="Motivos frecuentes">
                    {MOTIVOS_RECHAZO.map((m) => (
                      <button
                        key={m}
                        type="button"
                        className={
                          motivo === m ? `${styles.chip} ${styles.chipActivo}` : styles.chip
                        }
                        aria-pressed={motivo === m}
                        disabled={hayPeticionEnVuelo}
                        onClick={() => {
                          setMotivo(m)
                          if (errorMotivo) setErrorMotivo(null)
                        }}
                      >
                        {m}
                      </button>
                    ))}
                  </div>
                  <textarea
                    id={`motivo-${req.requisicionId}`}
                    className={styles.textareaMotivo}
                    maxLength={500}
                    value={motivo}
                    onChange={(e) => {
                      setMotivo(e.target.value)
                      if (errorMotivo) setErrorMotivo(null)
                    }}
                    placeholder="Elegí un motivo de arriba o escribí el tuyo"
                    disabled={hayPeticionEnVuelo}
                  />
                  <div className={styles.contador}>{motivo.length}/500</div>
                  {errorMotivo && (
                    <p className={styles.errorMotivo} role="alert">
                      {errorMotivo}
                    </p>
                  )}
                  <div className={styles.accionesRechazo}>
                    <button
                      type="button"
                      className={styles.botonConfirmar}
                      disabled={hayPeticionEnVuelo}
                      onClick={() => void confirmarRechazo(req)}
                    >
                      {operacionEnVuelo?.tipo === 'rechazar' && operacionEnVuelo?.id === req.requisicionId
                        ? 'Rechazando…'
                        : 'Confirmar rechazo'}
                    </button>
                    <button
                      type="button"
                      className={styles.botonCancelar}
                      disabled={hayPeticionEnVuelo}
                      onClick={cancelarRechazo}
                    >
                      Cancelar
                    </button>
                  </div>
                </div>
              ) : (
                <div className={styles.acciones}>
                  <button
                    type="button"
                    className={styles.boton}
                    disabled={hayPeticionEnVuelo}
                    aria-label={`Aprobar ${req.numeroRequisicion}`}
                    onClick={() => void aprobar(req)}
                  >
                    {operacionEnVuelo?.tipo === 'aprobar' && operacionEnVuelo?.id === req.requisicionId
                      ? 'Aprobando…'
                      : 'Aprobar'}
                  </button>
                  <button
                    type="button"
                    className={styles.botonRechazar}
                    disabled={hayPeticionEnVuelo}
                    aria-label={`Rechazar ${req.numeroRequisicion}`}
                    onClick={() => iniciarRechazo(req.requisicionId)}
                  >
                    Rechazar
                  </button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}

      {!cargando && !errorCarga && (
        <BotonVerMas
          cargadas={pendientes.length}
          total={total}
          cargando={cargandoMas}
          error={errorMas}
          onVerMas={() => void verMas()}
        />
      )}
    </section>
  )
}
