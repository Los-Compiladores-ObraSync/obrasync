import { type FormEvent, useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { ApiError } from '../api/ApiError'
import { BotonVerMas } from '../componentes/BotonVerMas'
import { ESPERA_BUSQUEDA_MS } from '../requisiciones/configuracionBusqueda'
import {
  crearRequisicion,
  editarYReenviarRequisicion,
  generarIdempotencyKey,
  obtenerInsumosCatalogo,
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

/** Lo que devolvió el servidor para un texto de búsqueda (#57). */
interface ResultadoBusqueda {
  termino: string
  lista: InsumoCatalogo[]
  fallo: boolean
  /** Total que informa el servicio para esa búsqueda (#172). Sin definir si no lo envía. */
  total?: number
  /** Última página traída (1 = la primera). */
  pagina: number
}

/** Suma insumos a los ya vistos (sin repetir): ver el estado "vistos" en la pantalla. */
function sumarVistos(
  actuales: Record<string, InsumoCatalogo>,
  lista: InsumoCatalogo[],
): Record<string, InsumoCatalogo> {
  return {
    ...actuales,
    ...Object.fromEntries(lista.map((ins) => [ins.articuloId, ins])),
  }
}

/** Agrega una página nueva a lo ya mostrado, sin repetir insumos. */
function sumarInsumos(actuales: InsumoCatalogo[], nuevos: InsumoCatalogo[]): InsumoCatalogo[] {
  const ids = new Set(actuales.map((ins) => ins.articuloId))
  return [...actuales, ...nuevos.filter((ins) => !ids.has(ins.articuloId))]
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
  // Catálogo sin búsqueda: llega de a una página (#172) y "Ver más" pide la siguiente.
  const [catalogo, setCatalogo] = useState<InsumoCatalogo[]>([])
  const [totalCatalogo, setTotalCatalogo] = useState<number | undefined>(undefined)
  const [paginaCatalogo, setPaginaCatalogo] = useState(1)
  const [cargandoMasCatalogo, setCargandoMasCatalogo] = useState(false)
  const [errorMasCatalogo, setErrorMasCatalogo] = useState(false)
  const [cargandoMasResultados, setCargandoMasResultados] = useState(false)
  const [errorMasResultados, setErrorMasResultados] = useState(false)
  // Todos los insumos que se vieron alguna vez (catálogo y búsquedas): así un insumo elegido en
  // una búsqueda no se "pierde" aunque no esté en la primera página del catálogo.
  const [vistos, setVistos] = useState<Record<string, InsumoCatalogo>>({})
  const [cargandoInicial, setCargandoInicial] = useState(true)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)

  const [busqueda, setBusqueda] = useState('')
  const [resultadoBusqueda, setResultadoBusqueda] =
    useState<ResultadoBusqueda | null>(null)

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
  const enviandoRef = useRef(false)
  const idempotencyKeyRef = useRef(generarIdempotencyKey())
  const [requisicionCreada, setRequisicionCreada] =
    useState<RequisicionResponseDto | null>(null)

  function reintentarCarga() {
    setCargandoInicial(true)
    setErrorCarga(null)
    void Promise.all([obtenerObras(), obtenerInsumosCatalogo()])
      .then(([listaObras, paginaInsumos]) => {
        setObras(listaObras)
        setCatalogo(paginaInsumos.items)
        setTotalCatalogo(paginaInsumos.total)
        setPaginaCatalogo(1)
        setVistos((actuales) => sumarVistos(actuales, paginaInsumos.items))
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
      .then(([listaObras, paginaInsumos]) => {
        if (!cancelado) {
          setObras(listaObras)
          setCatalogo(paginaInsumos.items)
          setTotalCatalogo(paginaInsumos.total)
          setPaginaCatalogo(1)
          setVistos((actuales) => sumarVistos(actuales, paginaInsumos.items))
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

  // Búsqueda de insumos (#57): se le pregunta al servidor con ?search=. Con el
  // buscador vacío no se pide nada: se usa el catálogo completo ya cargado.
  const termino = busqueda.trim()
  const buscadorRef = useRef<HTMLInputElement>(null)

  // Lo escrito, pero recién después de una pausa (#58): así no se pide en cada letra.
  const [terminoConEspera, setTerminoConEspera] = useState('')

  useEffect(() => {
    const temporizador = setTimeout(
      () => setTerminoConEspera(termino),
      ESPERA_BUSQUEDA_MS,
    )
    return () => clearTimeout(temporizador)
  }, [termino])

  // Si sale una búsqueda nueva mientras la anterior no volvió, la respuesta vieja
  // se descarta (el flag "cancelado", igual que en AprobarRequisicionesPage).
  useEffect(() => {
    if (terminoConEspera === '') return
    let cancelado = false

    obtenerInsumosCatalogo(terminoConEspera)
      .then((pagina) => {
        if (!cancelado) {
          setResultadoBusqueda({
            termino: terminoConEspera,
            lista: pagina.items,
            total: pagina.total,
            pagina: 1,
            fallo: false,
          })
          setVistos((actuales) => sumarVistos(actuales, pagina.items))
        }
      })
      .catch(() => {
        if (!cancelado) {
          setResultadoBusqueda({ termino: terminoConEspera, lista: [], pagina: 1, fallo: true })
        }
      })

    return () => {
      cancelado = true
    }
  }, [terminoConEspera])

  // Se calculan a partir de lo que ya hay, sin guardarlos en otro estado.
  const resultadoVigente =
    resultadoBusqueda?.termino === termino ? resultadoBusqueda : null
  const buscando = termino !== '' && resultadoVigente === null
  const falloBusqueda = resultadoVigente?.fallo === true
  // Lo que encontró la búsqueda, para mostrarlo a la vista (no solo dentro del select).
  const resultados =
    resultadoVigente !== null && !resultadoVigente.fallo ? resultadoVigente.lista : []
  const sinResultados =
    resultadoVigente !== null &&
    !resultadoVigente.fallo &&
    resultadoVigente.lista.length === 0
  const idsElegidos = new Set(items.map((it) => it.articuloId).filter(Boolean))
  // Mientras llega la respuesta se siguen mostrando los últimos resultados.
  const insumosOfrecidos =
    termino === '' ? catalogo : (resultadoBusqueda?.lista ?? catalogo)

  const obraSeleccionada = obras.find((o) => o.id === obraId)
  const etapasDisponibles = obraSeleccionada ? obraSeleccionada.etapas : []

  /** "Ver más" del catálogo sin búsqueda (#172): pide la página siguiente. */
  async function verMasCatalogo() {
    if (cargandoMasCatalogo) return
    setCargandoMasCatalogo(true)
    setErrorMasCatalogo(false)
    try {
      const pagina = await obtenerInsumosCatalogo(undefined, true, paginaCatalogo + 1)
      setCatalogo((actuales) => sumarInsumos(actuales, pagina.items))
      setTotalCatalogo(pagina.total)
      setPaginaCatalogo((n) => n + 1)
      setVistos((actuales) => sumarVistos(actuales, pagina.items))
    } catch {
      setErrorMasCatalogo(true)
    } finally {
      setCargandoMasCatalogo(false)
    }
  }

  /** "Ver más" de los resultados de la búsqueda actual (#172). */
  async function verMasResultados() {
    const vigente = resultadoBusqueda
    if (cargandoMasResultados || !vigente || vigente.termino !== termino) return
    setCargandoMasResultados(true)
    setErrorMasResultados(false)
    try {
      const pagina = await obtenerInsumosCatalogo(vigente.termino, true, vigente.pagina + 1)
      // Si mientras tanto se buscó otra cosa, esta respuesta ya no corresponde.
      setResultadoBusqueda((actual) =>
        actual && actual.termino === vigente.termino
          ? {
              ...actual,
              lista: sumarInsumos(actual.lista, pagina.items),
              total: pagina.total,
              pagina: actual.pagina + 1,
            }
          : actual,
      )
      setVistos((actuales) => sumarVistos(actuales, pagina.items))
    } catch {
      setErrorMasResultados(true)
    } finally {
      setCargandoMasResultados(false)
    }
  }

  function handleCambioObra(nuevaObraId: string) {
    setObraId(nuevaObraId)
    setEtapa('')
    idempotencyKeyRef.current = generarIdempotencyKey()
  }

  function agregarFila() {
    idempotencyKeyRef.current = generarIdempotencyKey()
    setItems((actuales) => [
      ...actuales,
      { id: siguienteFilaId.current++, articuloId: '', cantidad: '' },
    ])
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

  /** Elige un resultado de la búsqueda: va a la primera fila sin insumo o, si
   *  todas tienen, se agrega una fila nueva. */
  function elegirResultado(articuloId: string) {
    idempotencyKeyRef.current = generarIdempotencyKey()
    const filaVacia = items.find((it) => it.articuloId === '')
    if (filaVacia) {
      cambiarArticulo(filaVacia.id, articuloId)
    } else {
      setItems((actuales) => [
        ...actuales,
        { id: siguienteFilaId.current++, articuloId, cantidad: '' },
      ])
    }
    // Se vacía el buscador y se deja el cursor ahí: para el siguiente material (cemento, después cal)
    // no hace falta subir a borrar lo anterior.
    setBusqueda('')
    buscadorRef.current?.focus()
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

          <div className={styles.grupo}>
            <label htmlFor="req-buscar-insumo" className={styles.label}>
              Buscar insumo
            </label>
            <div className={styles.filaBuscador}>
              <input
                ref={buscadorRef}
                id="req-buscar-insumo"
                type="search"
                className={`${styles.input} ${styles.buscador}`}
                value={busqueda}
                onChange={(e) => setBusqueda(e.target.value)}
                // Enter dentro de un formulario lo envía: acá solo se está buscando.
                onKeyDown={(e) => {
                  if (e.key === 'Enter') e.preventDefault()
                }}
                placeholder="Código o nombre (por ejemplo: cemento)"
                autoComplete="off"
              />
              {busqueda !== '' && (
                <button
                  type="button"
                  className={styles.secundario}
                  aria-label="Limpiar búsqueda"
                  onClick={() => {
                    setBusqueda('')
                    buscadorRef.current?.focus()
                  }}
                >
                  Limpiar
                </button>
              )}
            </div>
            {buscando && (
              <p role="status" className={styles.ayudaBusqueda}>
                Buscando…
              </p>
            )}
            {falloBusqueda && (
              <p role="alert" className={styles.errorCampo}>
                No se pudo buscar insumos. Intentá de nuevo.
              </p>
            )}
            {sinResultados && (
              <p role="status" className={styles.ayudaBusqueda}>
                No encontramos insumos con ese nombre
              </p>
            )}
            {resultados.length > 0 && (
              <>
                <p role="status" className={styles.ayudaBusqueda}>
                  {resultados.length === 1
                    ? '1 insumo encontrado.'
                    : `${resultados.length} insumos encontrados.`}{' '}
                  Tocá uno para agregarlo.
                </p>
                <ul className={styles.listaResultados}>
                  {resultados.map((ins) => {
                    const yaAgregado = idsElegidos.has(ins.articuloId)
                    return (
                      <li key={ins.articuloId}>
                        <button
                          type="button"
                          className={`${styles.secundario} ${styles.resultado}`}
                          disabled={yaAgregado}
                          onClick={() => elegirResultado(ins.articuloId)}
                        >
                          {ins.codigo} — {ins.descripcion}
                          {yaAgregado ? ' (ya agregado)' : ''}
                        </button>
                      </li>
                    )
                  })}
                </ul>
              </>
            )}
            {termino === '' && (
              <BotonVerMas
                cargadas={catalogo.length}
                total={totalCatalogo}
                nombre="insumos"
                cargando={cargandoMasCatalogo}
                error={errorMasCatalogo}
                onVerMas={() => void verMasCatalogo()}
              />
            )}
            {resultadoVigente !== null && !resultadoVigente.fallo && (
              <BotonVerMas
                cargadas={resultadoVigente.lista.length}
                total={resultadoVigente.total}
                nombre="insumos"
                cargando={cargandoMasResultados}
                error={errorMasResultados}
                onVerMas={() => void verMasResultados()}
              />
            )}
          </div>

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

            const otrosSeleccionados = new Set(
              items
                .filter((it) => it.id !== item.id && it.articuloId)
                .map((it) => it.articuloId),
            )
            const coincidentes = insumosOfrecidos.filter(
              (ins) =>
                ins.articuloId === item.articuloId ||
                !otrosSeleccionados.has(ins.articuloId),
            )
            // El insumo ya elegido en esta fila tiene que seguir en su lista aunque
            // la búsqueda actual no lo traiga; si no, el select quedaría sin valor.
            const insumosDisponibles =
              insumoSeleccionado &&
              !coincidentes.some((ins) => ins.articuloId === insumoSeleccionado.articuloId)
                ? [insumoSeleccionado, ...coincidentes]
                : coincidentes

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
          {enviando ? 'Enviando…' : requisicionIdEdicion ? 'Guardar y reenviar' : 'Enviar requisición'}
        </button>
      </form>
    </section>
  )
}
