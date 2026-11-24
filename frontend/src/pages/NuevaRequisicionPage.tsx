import { type FormEvent, useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { ApiError } from '../api/ApiError'
import { BuscadorInsumo } from '../requisiciones/BuscadorInsumo'
import {
  crearRequisicion,
  editarYReenviarRequisicion,
  generarIdempotencyKey,
  obtenerObras,
  obtenerRequisicion,
} from '../requisiciones/requisicionesService'
import type {
  InsumoCatalogo,
  ObraResumen,
  RequisicionResponseDto,
} from '../requisiciones/requisicionesTipos'
import {
  hayErrores,
  mapearEstadoRequisicion,
  validarRequisicion,
  type ErroresRequisicion,
} from '../requisiciones/validacionRequisicion'
import { cantidadConUnidad, etiquetaUnidad } from '../requisiciones/unidades'
import styles from './NuevaRequisicionPage.module.css'

interface FilaItemForm {
  id: number
  articuloId: string
  cantidad: string
}

function mensajeDeErrorApi(err: unknown, editando = false): string {
  if (err instanceof ApiError) {
    if (err.status === 400) {
      return err.detail ?? 'Hay datos inválidos en la requisición. Revisá el formulario.'
    }
    if (err.status === 403) {
      return editando
        ? 'La requisición no pertenece al Capataz o no tenés permiso para editarla.'
        : 'No tenés permiso para crear requisiciones.'
    }
    if (err.status === 404) {
      return err.detail ?? 'La obra o algún artículo ya no existe. Recargá la página e intentá de nuevo.'
    }
    if (err.status === 409) {
      if (editando) return 'La requisición ya fue aprobada, rechazada o cancelada y no puede editarse.'
      return err.detail ?? 'La requisición entra en conflicto con el estado actual. Revisá los datos.'
    }
  }
  return 'No se pudo conectar con el servidor. Intentá nuevamente.'
}

export function NuevaRequisicionPage() {
  const [params] = useSearchParams()
  const requisicionIdEdicion = params.get('editar')
  // "Repetir un pedido anterior": precarga los mismos materiales, pero crea una requisición NUEVA
  // (no toca la original). Sirve con cualquier estado del pedido de origen.
  const requisicionIdRepetir = requisicionIdEdicion ? null : params.get('repetir')
  const idOrigen = requisicionIdEdicion ?? requisicionIdRepetir
  const rutaHistorial = params.get('volver') || '/obra/requisiciones'
  const [cargandoEdicion, setCargandoEdicion] = useState(!!idOrigen)
  const [obras, setObras] = useState<ObraResumen[]>([])
  // Los insumos elegidos (o precargados al editar/repetir un pedido): de acá salen el nombre y la
  // unidad de medida que se muestran en cada fila.
  const [vistos, setVistos] = useState<Record<string, InsumoCatalogo>>({})
  const [cargandoInicial, setCargandoInicial] = useState(true)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)

  const [obraId, setObraId] = useState('')
  const [etapa, setEtapa] = useState('')
  const [observaciones, setObservaciones] = useState('')

  const siguienteFilaId = useRef(2)
  // Foco al cargar varios insumos seguidos (#237): la fila recién agregada enfoca su buscador y,
  // al elegir un insumo, el cursor pasa a la cantidad de esa fila.
  const [filaNuevaId, setFilaNuevaId] = useState<number | null>(null)
  const enfocarCantidadDe = useRef<number | null>(null)
  const [items, setItems] = useState<FilaItemForm[]>([
    { id: 1, articuloId: '', cantidad: '' },
  ])

  const [errores, setErrores] = useState<ErroresRequisicion>({})
  const [apiError, setApiError] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const enviandoRef = useRef(false)
  const idempotencyKeyRef = useRef(generarIdempotencyKey())
  const [requisicionCreada, setRequisicionCreada] =
    useState<RequisicionResponseDto | null>(null)

  function reintentarCarga() {
    setCargandoInicial(true)
    setErrorCarga(null)
    void obtenerObras()
      .then((listaObras) => {
        setObras(listaObras)
        setCargandoInicial(false)
      })
      .catch(() => {
        setErrorCarga('No se pudieron cargar los datos necesarios.')
        setCargandoInicial(false)
      })
  }

  useEffect(() => {
    let cancelado = false

    obtenerObras()
      .then((listaObras) => {
        if (!cancelado) {
          setObras(listaObras)
          setCargandoInicial(false)
        }
      })
      .catch(() => {
        if (!cancelado) {
          setErrorCarga('No se pudieron cargar los datos necesarios.')
          setCargandoInicial(false)
        }
      })

    return () => {
      cancelado = true
    }
  }, [])

  useEffect(() => {
    if (!idOrigen) return
    let cancelado = false
    obtenerRequisicion(idOrigen)
      .then((requisicion) => {
        if (cancelado) return
        if (requisicionIdEdicion && requisicion.estado !== 'Rechazada' && requisicion.estado !== 'Pendiente') {
          setErrorCarga('La requisición ya fue aprobada o cancelada y no puede editarse.')
        } else {
          setObraId(requisicion.obraId)
          setEtapa(requisicion.etapa ?? '')
          setObservaciones(requisicion.observaciones ?? '')
          setItems(requisicion.items.map((item, index) => ({
            id: index + 1,
            articuloId: item.articuloId,
            cantidad: String(item.cantidad),
          })))
          siguienteFilaId.current = requisicion.items.length + 1
          setVistos((actuales) => ({
            ...actuales,
            ...Object.fromEntries(requisicion.items.map((item) => [item.articuloId, {
              articuloId: item.articuloId, codigo: item.articuloId, descripcion: item.descripcion,
              unidadMedida: item.unidadMedida, moneda: '', activo: true,
            } satisfies InsumoCatalogo])),
          }))
        }
      })
      .catch((err: unknown) => {
        if (!cancelado) setErrorCarga(err instanceof ApiError && err.status === 403
          ? (requisicionIdRepetir
              ? 'Ese pedido no es tuyo o no tenés permiso para repetirlo.'
              : 'La requisición no pertenece al Capataz o no tenés permiso para editarla.')
          : (requisicionIdRepetir
              ? 'No se pudo cargar el pedido anterior para repetirlo.'
              : 'No se pudo cargar la requisición para editarla.'))
      })
      .finally(() => { if (!cancelado) setCargandoEdicion(false) })
    return () => { cancelado = true }
  }, [idOrigen, requisicionIdEdicion, requisicionIdRepetir])

  const obraSeleccionada = obras.find((o) => o.id === obraId)
  const etapasDisponibles = obraSeleccionada ? obraSeleccionada.etapas : []

  function handleCambioObra(nuevaObraId: string) {
    setObraId(nuevaObraId)
    setEtapa('')
    idempotencyKeyRef.current = generarIdempotencyKey()
  }

  function agregarFila() {
    idempotencyKeyRef.current = generarIdempotencyKey()
    const nuevaId = siguienteFilaId.current++
    setFilaNuevaId(nuevaId)
    setItems((actuales) => [...actuales, { id: nuevaId, articuloId: '', cantidad: '' }])
  }

  function quitarFila(id: number) {
    if (items.length <= 1) return
    idempotencyKeyRef.current = generarIdempotencyKey()
    setItems((actuales) => actuales.filter((it) => it.id !== id))
  }

  function cambiarArticulo(id: number, nuevoArticuloId: string) {
    idempotencyKeyRef.current = generarIdempotencyKey()
    setItems((actuales) =>
      actuales.map((it) =>
        it.id === id ? { ...it, articuloId: nuevoArticuloId } : it,
      ),
    )
  }

  function cambiarCantidad(id: number, nuevaCantidad: string) {
    idempotencyKeyRef.current = generarIdempotencyKey()
    setItems((actuales) =>
      actuales.map((it) =>
        it.id === id ? { ...it, cantidad: nuevaCantidad } : it,
      ),
    )
  }

  /** Se eligió un resultado en el buscador de una fila: queda en esa fila y se recuerda para mostrar
   *  su nombre y su unidad de medida. */
  function elegirInsumo(id: number, insumo: InsumoCatalogo) {
    setVistos((actuales) => ({ ...actuales, [insumo.articuloId]: insumo }))
    cambiarArticulo(id, insumo.articuloId)
    enfocarCantidadDe.current = id
  }

  // Cuando la fila ya muestra el insumo elegido, el cursor va a su cantidad.
  useEffect(() => {
    if (enfocarCantidadDe.current === null) return
    document.getElementById(`item-cantidad-${enfocarCantidadDe.current}`)?.focus()
    enfocarCantidadDe.current = null
  }, [items])

  function limpiarFormulario() {
    setObraId('')
    setEtapa('')
    setObservaciones('')
    siguienteFilaId.current = 2
    setItems([{ id: 1, articuloId: '', cantidad: '' }])
    setErrores({})
    setApiError(null)
    setRequisicionCreada(null)
    idempotencyKeyRef.current = generarIdempotencyKey()
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (enviandoRef.current) return
    setApiError(null)

    const erroresForm = validarRequisicion({
      obraId,
      etapa,
      observaciones,
      items: items.map((it) => ({
        articuloId: it.articuloId,
        cantidad: it.cantidad,
      })),
    })

    if (hayErrores(erroresForm)) {
      setErrores(erroresForm)
      return
    }

    setErrores({})
    enviandoRef.current = true
    setEnviando(true)

    try {
      const itemsDto = items.map((it) => ({
        articuloId: it.articuloId,
        cantidad: Number(it.cantidad),
      }))
      const creada = requisicionIdEdicion
        ? await editarYReenviarRequisicion(requisicionIdEdicion, {
            etapa: etapa.trim(),
            observaciones: observaciones.trim() || null,
            items: itemsDto,
          })
        : await crearRequisicion({
            obraId,
            etapa: etapa.trim(),
            observaciones: observaciones.trim() || null,
            items: itemsDto,
            ...(requisicionIdRepetir ? { repeticionIntencional: true } : {}),
          }, idempotencyKeyRef.current)
      setRequisicionCreada(creada)
    } catch (err) {
      setApiError(mensajeDeErrorApi(err, !!requisicionIdEdicion))
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  /** Suma o resta a la cantidad de una fila (para los botones − y +). Nunca baja de 0. */
  function ajustarCantidad(id: number, delta: number) {
    const fila = items.find((it) => it.id === id)
    const actual = parseFloat((fila?.cantidad ?? '').replace(',', '.'))
    const base = Number.isFinite(actual) ? actual : 0
    const nuevo = Math.max(0, Math.round((base + delta) * 10000) / 10000)
    cambiarCantidad(id, String(nuevo))
  }

  function claseSelect(conError: boolean) {
    return conError ? `${styles.select} ${styles.inputInvalido}` : styles.select
  }

  function claseInput(conError: boolean) {
    return conError ? `${styles.input} ${styles.inputInvalido}` : styles.input
  }

  if (cargandoInicial || cargandoEdicion) {
    return (
      <section className={styles.contenedor}>
        <p role="status">Cargando…</p>
      </section>
    )
  }

  if (errorCarga) {
    return (
      <section className={styles.contenedor}>
        <div className={styles.estadoCarga}>
          <p role="alert" className={styles.errorApi}>
            {errorCarga}
          </p>
          <button
            type="button"
            className={styles.secundario}
            onClick={reintentarCarga}
          >
            Reintentar
          </button>
        </div>
      </section>
    )
  }

  if (requisicionCreada) {
    const nombreObra = obraSeleccionada?.nombre ?? requisicionCreada.obraId
    return (
      <section className={styles.contenedor}>
        <div className={styles.tarjetaConfirmacion} role="status">
          <div className={styles.cabeceraExito}>
            <h1 className={styles.tituloExito}>Requisición enviada con éxito</h1>
            <p className={styles.subtituloExito}>
              Número: <strong>{requisicionCreada.numeroRequisicion}</strong>
            </p>
          </div>

          <div>
            <span className={styles.badgeEstado}>
              {mapearEstadoRequisicion(requisicionCreada.estado)}
            </span>
          </div>

          <div className={styles.datosConfirmacion}>
            <p>
              <strong>Obra:</strong> {nombreObra}
            </p>
            {requisicionCreada.etapa && (
              <p>
                <strong>Etapa:</strong> {requisicionCreada.etapa}
              </p>
            )}
            {requisicionCreada.observaciones && (
              <p>
                <strong>Observaciones:</strong> {requisicionCreada.observaciones}
              </p>
            )}
            <p>
              <strong>Materiales solicitados:</strong>
            </p>
            <ul className={styles.listaItemsConfirmados}>
              {requisicionCreada.items.map((it) => (
                <li key={it.articuloId}>
                  {cantidadConUnidad(it.cantidad, it.unidadMedida)} — {it.descripcion}
                </li>
              ))}
            </ul>
          </div>

          <div className={styles.accionesExito}>
            {!requisicionIdEdicion && requisicionCreada.estado === 'Pendiente' && (
              <Link
                to={`/obra/requisicion/nueva?editar=${encodeURIComponent(requisicionCreada.requisicionId)}`}
                className={styles.enlaceBoton}
                onClick={(event) => {
                  if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return
                  setCargandoEdicion(true)
                  setRequisicionCreada(null)
                }}
              >
                Editar requisición
              </Link>
            )}
            {requisicionIdEdicion && (
              <Link to={rutaHistorial} className={styles.enlaceBoton}>
                Volver al historial
              </Link>
            )}
            <button
              type="button"
              className={styles.secundario}
              onClick={limpiarFormulario}
            >
              Cargar otra requisición
            </button>
            {!requisicionIdEdicion && <Link to="/" className={styles.enlaceBoton}>Volver al inicio</Link>}
          </div>
        </div>
      </section>
    )
  }

  return (
    <section className={styles.contenedor}>
      <Link to="/" className={styles.volver}>
        ← Volver al inicio
      </Link>
      <h1>
        {requisicionIdEdicion
          ? 'Editar y reenviar requisición'
          : requisicionIdRepetir
            ? 'Repetir pedido'
            : 'Nueva requisición'}
      </h1>
      {requisicionIdRepetir && (
        <p className={styles.aviso} role="note">
          Cargamos los mismos materiales de tu pedido anterior. Revisalos, ajustá lo que haga falta y
          enviá: se crea un pedido nuevo.
        </p>
      )}

      <form className={styles.formulario} onSubmit={handleSubmit} noValidate>
        {apiError && (
          <p className={styles.errorApi} role="alert">
            {apiError}
          </p>
        )}

        <div className={styles.grupo}>
          <label htmlFor="req-obra" className={styles.label}>
            Obra
          </label>
          <select
            id="req-obra"
            className={claseSelect(!!errores.obraId)}
            value={obraId}
            onChange={(e) => handleCambioObra(e.target.value)}
            aria-invalid={!!errores.obraId}
            aria-describedby={errores.obraId ? 'req-obra-error' : undefined}
          >
            <option value="">-- Seleccioná una obra --</option>
            {obras.map((o) => (
              <option key={o.id} value={o.id}>
                {o.nombre}
              </option>
            ))}
          </select>
          {errores.obraId && (
            <p id="req-obra-error" className={styles.errorCampo} role="alert">
              {errores.obraId}
            </p>
          )}
        </div>

        <div className={styles.grupo}>
          <label htmlFor="req-etapa" className={styles.label}>
            Etapa de la obra
          </label>
          <select
            id="req-etapa"
            className={claseSelect(!!errores.etapa)}
            value={etapa}
            onChange={(e) => {
              setEtapa(e.target.value)
              idempotencyKeyRef.current = generarIdempotencyKey()
            }}
            disabled={!obraId}
            aria-invalid={!!errores.etapa}
            aria-describedby={errores.etapa ? 'req-etapa-error' : undefined}
          >
            <option value="">
              {!obraId
                ? '-- Primero seleccioná una obra --'
                : '-- Seleccioná una etapa --'}
            </option>
            {etapasDisponibles.map((et) => (
              <option key={et.id} value={et.nombreEtapa}>
                {et.nombreEtapa}
              </option>
            ))}
          </select>
          {errores.etapa && (
            <p id="req-etapa-error" className={styles.errorCampo} role="alert">
              {errores.etapa}
            </p>
          )}
        </div>

        <fieldset className={styles.itemsFieldset}>
          <legend className={styles.itemsLegend}>Insumos y materiales</legend>

          {errores.itemsGeneral && (
            <p className={styles.errorCampo} role="alert">
              {errores.itemsGeneral}
            </p>
          )}

          {items.map((item, index) => {
            const errorItem = errores.items?.[index]
            const insumoSeleccionado = vistos[item.articuloId]
            const unidadMedida = insumoSeleccionado
              ? etiquetaUnidad(insumoSeleccionado.unidadMedida)
              : '—'

            // Los insumos de las OTRAS filas: no se pueden elegir dos veces.
            const ocupados = new Set(
              items.filter((it) => it.id !== item.id && it.articuloId).map((it) => it.articuloId),
            )

            return (
              <div key={item.id} className={styles.filaItem}>
                <BuscadorInsumo
                  numero={index + 1}
                  id={`item-insumo-${item.id}`}
                  seleccionado={insumoSeleccionado}
                  ocupados={ocupados}
                  error={errorItem?.articuloId}
                  onElegir={(insumo) => elegirInsumo(item.id, insumo)}
                  onCambiar={() => cambiarArticulo(item.id, '')}
                  enfocarAlAparecer={item.id === filaNuevaId}
                />

                <div className={styles.filaItemCantidad}>
                  <div className={styles.colCantidad}>
                    <label
                      htmlFor={`item-cantidad-${item.id}`}
                      className={styles.label}
                    >
                      Cantidad
                    </label>
                    <div className={styles.stepper}>
                      <button
                        type="button"
                        className={styles.stepperBoton}
                        aria-label={`Restar uno al insumo ${index + 1}`}
                        onClick={() => ajustarCantidad(item.id, -1)}
                      >
                        −
                      </button>
                      <input
                        id={`item-cantidad-${item.id}`}
                        type="number"
                        step="any"
                        min="0"
                        inputMode="decimal"
                        className={`${claseInput(!!errorItem?.cantidad)} ${styles.inputCantidad}`}
                        value={item.cantidad}
                        onChange={(e) => cambiarCantidad(item.id, e.target.value)}
                        // Enter con insumo y cantidad cargados agrega la fila siguiente (#237); si falta algo, no hace nada.
                        onKeyDown={(e) => {
                          if (e.key !== 'Enter') return
                          e.preventDefault()
                          if (item.articuloId && Number(item.cantidad) > 0) agregarFila()
                        }}
                        placeholder="0"
                        aria-invalid={!!errorItem?.cantidad}
                        aria-describedby={
                          errorItem?.cantidad
                            ? `item-cantidad-error-${item.id}`
                            : undefined
                        }
                      />
                      <button
                        type="button"
                        className={styles.stepperBoton}
                        aria-label={`Sumar uno al insumo ${index + 1}`}
                        onClick={() => ajustarCantidad(item.id, 1)}
                      >
                        +
                      </button>
                    </div>
                  </div>

                  <div
                    className={styles.unidadBadge}
                    title="Unidad de medida tomada del catálogo"
                  >
                    {unidadMedida}
                  </div>

                  <button
                    type="button"
                    className={styles.secundario}
                    aria-label={`Quitar insumo ${index + 1}`}
                    onClick={() => quitarFila(item.id)}
                    disabled={items.length <= 1}
                  >
                    Quitar
                  </button>
                </div>
                {errorItem?.cantidad && (
                  <p
                    id={`item-cantidad-error-${item.id}`}
                    className={styles.errorCampo}
                    role="alert"
                  >
                    {errorItem.cantidad}
                  </p>
                )}
              </div>
            )
          })}

          <button
            type="button"
            className={`${styles.secundario} ${styles.botonAgregar}`}
            onClick={agregarFila}
          >
            + Agregar otro insumo
          </button>
        </fieldset>

        <div className={styles.grupo}>
          <label htmlFor="req-observaciones" className={styles.label}>
            Observaciones (opcional)
          </label>
          <textarea
            id="req-observaciones"
            className={styles.textarea}
            value={observaciones}
            onChange={(e) => setObservaciones(e.target.value)}
            placeholder="Notas o detalles para la entrega"
          />
        </div>

        <button type="submit" className={styles.boton} disabled={enviando}>
          {enviando ? 'Enviando…' : requisicionIdEdicion ? 'Guardar y reenviar' : requisicionIdRepetir ? 'Crear nuevo pedido' : 'Enviar requisición'}
        </button>
      </form>
    </section>
  )
}
