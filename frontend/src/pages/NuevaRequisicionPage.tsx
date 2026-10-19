import { type FormEvent, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { ApiError } from '../api/ApiError'
import {
  crearRequisicion,
  obtenerInsumosCatalogo,
  obtenerObras,
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
import styles from './NuevaRequisicionPage.module.css'

interface FilaItemForm {
  id: number
  articuloId: string
  cantidad: string
}

function mensajeDeErrorApi(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 400) {
      return err.detail ?? 'Hay datos inválidos en la requisición. Revisá el formulario.'
    }
    if (err.status === 403) {
      return 'No tenés permiso para crear requisiciones.'
    }
  }
  return 'No se pudo conectar con el servidor. Intentá nuevamente.'
}

export function NuevaRequisicionPage() {
  const [obras, setObras] = useState<ObraResumen[]>([])
  const [catalogo, setCatalogo] = useState<InsumoCatalogo[]>([])
  const [cargandoInicial, setCargandoInicial] = useState(true)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)

  const [obraId, setObraId] = useState('')
  const [etapa, setEtapa] = useState('')
  const [observaciones, setObservaciones] = useState('')

  const siguienteFilaId = useRef(2)
  const [items, setItems] = useState<FilaItemForm[]>([
    { id: 1, articuloId: '', cantidad: '' },
  ])

  const [errores, setErrores] = useState<ErroresRequisicion>({})
  const [apiError, setApiError] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [requisicionCreada, setRequisicionCreada] =
    useState<RequisicionResponseDto | null>(null)

  function reintentarCarga() {
    setCargandoInicial(true)
    setErrorCarga(null)
    void Promise.all([obtenerObras(), obtenerInsumosCatalogo()])
      .then(([listaObras, listaInsumos]) => {
        setObras(listaObras)
        setCatalogo(listaInsumos)
        setCargandoInicial(false)
      })
      .catch(() => {
        setErrorCarga('No se pudieron cargar los datos necesarios.')
        setCargandoInicial(false)
      })
  }

  useEffect(() => {
    let cancelado = false

    Promise.all([obtenerObras(), obtenerInsumosCatalogo()])
      .then(([listaObras, listaInsumos]) => {
        if (!cancelado) {
          setObras(listaObras)
          setCatalogo(listaInsumos)
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

  const obraSeleccionada = obras.find((o) => o.id === obraId)
  const etapasDisponibles = obraSeleccionada ? obraSeleccionada.etapas : []

  function handleCambioObra(nuevaObraId: string) {
    setObraId(nuevaObraId)
    setEtapa('')
  }

  function agregarFila() {
    setItems((actuales) => [
      ...actuales,
      { id: siguienteFilaId.current++, articuloId: '', cantidad: '' },
    ])
  }

  function quitarFila(id: number) {
    if (items.length <= 1) return
    setItems((actuales) => actuales.filter((it) => it.id !== id))
  }

  function cambiarArticulo(id: number, nuevoArticuloId: string) {
    setItems((actuales) =>
      actuales.map((it) =>
        it.id === id ? { ...it, articuloId: nuevoArticuloId } : it,
      ),
    )
  }

  function cambiarCantidad(id: number, nuevaCantidad: string) {
    setItems((actuales) =>
      actuales.map((it) =>
        it.id === id ? { ...it, cantidad: nuevaCantidad } : it,
      ),
    )
  }

  function limpiarFormulario() {
    setObraId('')
    setEtapa('')
    setObservaciones('')
    siguienteFilaId.current = 2
    setItems([{ id: 1, articuloId: '', cantidad: '' }])
    setErrores({})
    setApiError(null)
    setRequisicionCreada(null)
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
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
    setEnviando(true)

    try {
      const creada = await crearRequisicion({
        obraId,
        etapa: etapa.trim(),
        observaciones: observaciones.trim() || null,
        items: items.map((it) => ({
          articuloId: it.articuloId,
          cantidad: Number(it.cantidad),
        })),
      })
      setRequisicionCreada(creada)
    } catch (err) {
      setApiError(mensajeDeErrorApi(err))
    } finally {
      setEnviando(false)
    }
  }

  function claseSelect(conError: boolean) {
    return conError ? `${styles.select} ${styles.inputInvalido}` : styles.select
  }

  function claseInput(conError: boolean) {
    return conError ? `${styles.input} ${styles.inputInvalido}` : styles.input
  }

  if (cargandoInicial) {
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
                  {it.cantidad} {it.unidadMedida} — {it.descripcion}
                </li>
              ))}
            </ul>
          </div>

          <div className={styles.accionesExito}>
            <button
              type="button"
              className={styles.secundario}
              onClick={limpiarFormulario}
            >
              Cargar otra requisición
            </button>
            <Link to="/obra" className={styles.enlaceBoton}>
              Volver a Obra
            </Link>
          </div>
        </div>
      </section>
    )
  }

  return (
    <section className={styles.contenedor}>
      <Link to="/obra" className={styles.volver}>
        ← Volver a Obra
      </Link>
      <h1>Nueva requisición</h1>

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
            onChange={(e) => setEtapa(e.target.value)}
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
            const insumoSeleccionado = catalogo.find(
              (ins) => ins.articuloId === item.articuloId,
            )
            const unidadMedida = insumoSeleccionado
              ? insumoSeleccionado.unidadMedida
              : '—'

            const otrosSeleccionados = new Set(
              items
                .filter((it) => it.id !== item.id && it.articuloId)
                .map((it) => it.articuloId),
            )
            const insumosDisponibles = catalogo.filter(
              (ins) =>
                ins.articuloId === item.articuloId ||
                !otrosSeleccionados.has(ins.articuloId),
            )

            return (
              <div key={item.id} className={styles.filaItem}>
                <div className={styles.grupo}>
                  <label
                    htmlFor={`item-insumo-${item.id}`}
                    className={styles.label}
                  >
                    Insumo #{index + 1}
                  </label>
                  <select
                    id={`item-insumo-${item.id}`}
                    className={claseSelect(!!errorItem?.articuloId)}
                    value={item.articuloId}
                    onChange={(e) => cambiarArticulo(item.id, e.target.value)}
                    aria-invalid={!!errorItem?.articuloId}
                    aria-describedby={
                      errorItem?.articuloId
                        ? `item-insumo-error-${item.id}`
                        : undefined
                    }
                  >
                    <option value="">-- Seleccioná un insumo --</option>
                    {insumosDisponibles.map((ins) => (
                      <option key={ins.articuloId} value={ins.articuloId}>
                        {ins.codigo} — {ins.descripcion}
                      </option>
                    ))}
                  </select>
                  {errorItem?.articuloId && (
                    <p
                      id={`item-insumo-error-${item.id}`}
                      className={styles.errorCampo}
                      role="alert"
                    >
                      {errorItem.articuloId}
                    </p>
                  )}
                </div>

                <div className={styles.filaItemCantidad}>
                  <div className={styles.colCantidad}>
                    <label
                      htmlFor={`item-cantidad-${item.id}`}
                      className={styles.label}
                    >
                      Cantidad
                    </label>
                    <input
                      id={`item-cantidad-${item.id}`}
                      type="number"
                      step="any"
                      min="0"
                      inputMode="decimal"
                      className={claseInput(!!errorItem?.cantidad)}
                      value={item.cantidad}
                      onChange={(e) => cambiarCantidad(item.id, e.target.value)}
                      placeholder="0.00"
                      aria-invalid={!!errorItem?.cantidad}
                      aria-describedby={
                        errorItem?.cantidad
                          ? `item-cantidad-error-${item.id}`
                          : undefined
                      }
                    />
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
          {enviando ? 'Enviando…' : 'Enviar requisición'}
        </button>
      </form>
    </section>
  )
}
