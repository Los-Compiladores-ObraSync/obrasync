import { useAvisos } from '../notificaciones/useAvisos'
import { type ChangeEvent, type FormEvent, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { ApiError } from '../api/ApiError'
import { esFechaFutura, esFechaPasada, fechaEnPalabras, hoyComoTexto } from '../obras/fechaEnPalabras'
import { fechaAIso } from '../obras/validacionObra'
import { EntregaConProblemaForm } from '../recepciones/EntregaConProblemaForm'
import { ErrorDeImagen, reducirImagen } from '../recepciones/imagenRemito'
import { LineaDeTiempoOrden } from '../recepciones/LineaDeTiempoOrden'
import { leerRemitoDeFoto, obtenerHistorialRecepciones, obtenerOrdenesPendientes, registrarRecepcion } from '../recepciones/recepcionesService'
import { ESTADO_ENTREGA_PARCIAL } from '../licitaciones/entregaParcial'
import type { ItemOrdenPendiente, OrdenPendiente, RecepcionHistorial, RecepcionRegistrada } from '../recepciones/recepcionesTipos'
import {
  hayErrores,
  LARGO_MAXIMO_OBSERVACIONES,
  LARGO_MAXIMO_REMITO,
  validarRecepcion,
  type ErroresRecepcion,
} from '../recepciones/validacionRecepcion'
import { fechaLarga } from '../requisiciones/formatoFecha'
import { obtenerObras } from '../requisiciones/requisicionesService'
import { cantidadConUnidad } from '../requisiciones/unidades'
import styles from './RecepcionPage.module.css'

function mensajeDeErrorApi(err: unknown): string {
  if (err instanceof ApiError) {
    // El servidor ya manda el motivo en español (orden ya recibida, remito repetido, entrega parcial…).
    if (err.status === 400 || err.status === 409) {
      return err.detail ?? 'No se pudo registrar la recepción. Revisá los datos.'
    }
    if (err.status === 403) {
      return 'No tenés permiso para registrar recepciones.'
    }
  }
  return 'No se pudo conectar con el servidor. Intentá nuevamente.'
}

function saldoPendiente(item: ItemOrdenPendiente): number {
  return item.cantidadPendiente ?? Math.max(item.cantidadEsperada - item.cantidadRecibida, 0)
}

/** Estado de la lectura del remito por foto (#209). */
type EstadoFoto =
  | { tipo: 'inactivo' }
  | { tipo: 'leyendo' }
  | { tipo: 'listo'; texto: string }
  | { tipo: 'error'; texto: string }

function mensajeDeErrorFoto(err: unknown): string {
  if (err instanceof ErrorDeImagen) return err.message
  if (err instanceof ApiError) {
    if (err.status === 400) return 'La foto no sirvió. Probá con otra más clara o cargá el remito a mano.'
    if (err.status === 403) return 'No tenés permiso para leer remitos.'
    if (err.status === 429) return 'Hay muchas lecturas en este momento. Probá en un minuto o cargá el remito a mano.'
    if (err.status === 503) return 'La lectura de remitos no está disponible ahora. Cargalo a mano.'
  }
  return 'No se pudo leer el remito. Cargalo a mano.'
}

/** Recibir mercadería en obra: permite registrar entregas parciales y consultar su historial. */
export function RecepcionPage() {
  const { refrescar: refrescarAvisos } = useAvisos()
  const [ordenes, setOrdenes] = useState<OrdenPendiente[]>([])
  const [nombresObras, setNombresObras] = useState<Record<string, string>>({})
  const [cargando, setCargando] = useState(true)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)
  // Cada vez que cambia "intento" se vuelve a pedir la lista (así funcionan "Reintentar" y
  // "Recibir otra orden").
  const [intento, setIntento] = useState(0)

  const [elegida, setElegida] = useState<OrdenPendiente | null>(null)
  const [numeroRemito, setNumeroRemito] = useState('')
  const [fecha, setFecha] = useState(hoyComoTexto)
  const [observaciones, setObservaciones] = useState('')
  const [cantidades, setCantidades] = useState<Record<string, string>>({})
  // Mercadería dañada o con diferencias: por material se marca el problema y se describe qué pasó.
  // Faltantes no necesitan marca: si llegó menos de lo pedido, el resto queda como saldo pendiente.
  const [conProblema, setConProblema] = useState<Record<string, boolean>>({})
  const [notasProblema, setNotasProblema] = useState<Record<string, string>>({})
  const [erroresCantidad, setErroresCantidad] = useState<Record<string, string | undefined>>({})
  const [historial, setHistorial] = useState<RecepcionHistorial[]>([])
  const [cargandoHistorial, setCargandoHistorial] = useState(false)
  const [errorHistorial, setErrorHistorial] = useState(false)
  const [errores, setErrores] = useState<ErroresRecepcion>({})
  const [errorApi, setErrorApi] = useState<string | null>(null)
  const [revisando, setRevisando] = useState(false)
  const [enviando, setEnviando] = useState(false)
  // Guard inmediato contra el doble envío (mismo criterio que #109): el estado `enviando` recién
  // cambia en el próximo render y un segundo toque podría colarse antes.
  const enviandoRef = useRef(false)
  const [registrada, setRegistrada] = useState<RecepcionRegistrada | null>(null)
  // Entrega con problema (#224): "No llegó nada", "Llegó incompleta", "Llegó dañada" o "No es lo pedido", con fotos.
  const [reportandoProblema, setReportandoProblema] = useState(false)
  const [problemaRegistrado, setProblemaRegistrado] = useState<RecepcionRegistrada | null>(null)

  useEffect(() => {
    if (!elegida) return
    let cancelado = false
    obtenerHistorialRecepciones(elegida.ordenCompraId).then((lista) => {
      if (!cancelado) setHistorial(lista)
    }).catch(() => {
      if (!cancelado) setErrorHistorial(true)
    }).finally(() => {
      if (!cancelado) setCargandoHistorial(false)
    })
    return () => { cancelado = true }
  }, [elegida])

  // Foto del remito (#209): opcional. Lee el número y la fecha y los carga como SUGERENCIA editable.
  const [foto, setFoto] = useState<EstadoFoto>({ tipo: 'inactivo' })
  const entradaFotoRef = useRef<HTMLInputElement>(null)
  // Número de la lectura vigente: si la persona cambia de orden mientras se lee, la respuesta vieja se ignora.
  const lecturaVigenteRef = useRef(0)

  useEffect(() => {
    let cancelado = false

    async function cargar() {
      try {
        const [lista, obras] = await Promise.all([
          obtenerOrdenesPendientes(),
          // Si falla la lista de obras no es grave: se muestra "Obra" sin nombre.
          obtenerObras().catch(() => []),
        ])
        if (cancelado) return
        setOrdenes(lista)
        setNombresObras(Object.fromEntries(obras.map((o) => [o.id, o.nombre])))
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

  function recargar() {
    setErrorCarga(null)
    setCargando(true)
    setIntento((n) => n + 1)
  }

  function descartarLecturaDeFoto() {
    lecturaVigenteRef.current += 1
    setFoto({ tipo: 'inactivo' })
  }

  async function handleFoto(e: ChangeEvent<HTMLInputElement>) {
    const archivo = e.target.files?.[0]
    // Se vacía el campo para poder elegir la misma foto otra vez.
    e.target.value = ''
    if (!archivo || foto.tipo === 'leyendo') return

    const lectura = ++lecturaVigenteRef.current
    setFoto({ tipo: 'leyendo' })
    try {
      const imagen = await reducirImagen(archivo)
      const leido = await leerRemitoDeFoto(imagen)
      if (lectura !== lecturaVigenteRef.current) return

      const numero = leido.numeroRemito?.trim().slice(0, LARGO_MAXIMO_REMITO) ?? ''
      // La fecha también pasa por la regla de #127: una fecha futura no se carga.
      const fechaLeida = leido.fecha && fechaEnPalabras(leido.fecha) && !esFechaFutura(leido.fecha) ? leido.fecha : ''

      if (numero) setNumeroRemito(numero)
      if (fechaLeida) setFecha(fechaLeida)
      setErrores((previos) => ({
        ...previos,
        ...(numero ? { numeroRemito: undefined } : {}),
        ...(fechaLeida ? { fecha: undefined } : {}),
      }))

      if (numero && fechaLeida) {
        setFoto({ tipo: 'listo', texto: 'Leí el número y la fecha del remito. Revisalos antes de confirmar.' })
      } else if (numero) {
        setFoto({ tipo: 'listo', texto: 'Leí el número del remito. La fecha no se pudo leer: revisala.' })
      } else if (fechaLeida) {
        setFoto({ tipo: 'listo', texto: 'Leí solo la fecha del remito. Cargá el número a mano.' })
      } else {
        setFoto({ tipo: 'error', texto: 'No pude leer el remito en la foto. Probá con otra más clara o cargalo a mano.' })
      }
    } catch (err) {
      if (lectura !== lecturaVigenteRef.current) return
      setFoto({ tipo: 'error', texto: mensajeDeErrorFoto(err) })
    }
  }

  function elegirOrden(orden: OrdenPendiente) {
    descartarLecturaDeFoto()
    setReportandoProblema(false)
    setProblemaRegistrado(null)
    setElegida(orden)
    setNumeroRemito('')
    setFecha(hoyComoTexto())
    setObservaciones('')
    setCantidades(Object.fromEntries(orden.items.map((item) => [item.articuloId, String(saldoPendiente(item))])))
    setConProblema({})
    setNotasProblema({})
    setErroresCantidad({})
    setHistorial([])
    setCargandoHistorial(true)
    setErrorHistorial(false)
    setErrores({})
    setErrorApi(null)
  }

  function recibirOtra() {
    descartarLecturaDeFoto()
    setRegistrada(null)
    setRevisando(false)
    setReportandoProblema(false)
    setProblemaRegistrado(null)
    setElegida(null)
    recargar()
  }

  function nombreDeObra(obraId: string): string {
    return nombresObras[obraId] ?? 'Obra'
  }

  function revisarRecepcion(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (enviandoRef.current || !elegida) return

    // 1. Validación local: no llama a la API si hay errores de campo.
    const erroresForm = validarRecepcion({ numeroRemito, fecha, observaciones })
    const nuevosErrores: Record<string, string | undefined> = {}
    elegida.items.forEach((item) => {
      const texto = cantidades[item.articuloId] ?? ''
      const valor = Number(texto)
      if (texto.trim() === '' || !Number.isFinite(valor)) nuevosErrores[item.articuloId] = 'Ingresá una cantidad válida.'
      else if (valor < 0) nuevosErrores[item.articuloId] = 'La cantidad no puede ser negativa.'
      else if (valor > saldoPendiente(item)) nuevosErrores[item.articuloId] = `No puede superar el saldo de ${saldoPendiente(item)}.`
      else if (conProblema[item.articuloId] && (notasProblema[item.articuloId] ?? '').trim() === '') nuevosErrores[item.articuloId] = 'Contanos qué pasó con este material (por ejemplo: 3 bolsas rotas).'
    })
    if (elegida.items.every((item) => Number(cantidades[item.articuloId] ?? 0) === 0)) {
      nuevosErrores.general = 'Ingresá una cantidad mayor que cero en al menos un artículo.'
    }
    setErroresCantidad(nuevosErrores)
    if (hayErrores(erroresForm)) {
      setErrores(erroresForm)
      return
    }
    if (Object.keys(nuevosErrores).length) return
    setErrores({})
    setErrorApi(null)
    setRevisando(true)
  }

  async function confirmarRecepcion() {
    if (enviandoRef.current || !elegida) return
    enviandoRef.current = true
    setEnviando(true)

    try {
      const creada = await registrarRecepcion({
        obraId: elegida.obraId,
        ordenCompraId: elegida.ordenCompraId,
        numeroRemito: numeroRemito.trim(),
        fechaRecepcion: fechaAIso(fecha),
        observaciones: observaciones.trim() || null,
        // Se envían todos los ítems y la cantidadEsperada original según el contrato.
        detalles: elegida.items.map((item) => ({
          articuloId: item.articuloId,
          descripcionArticulo: item.descripcionArticulo,
          cantidadEsperada: item.cantidadEsperada,
          cantidadRecibida: Number(cantidades[item.articuloId] ?? 0),
          unidadMedida: item.unidadMedida,
          conforme: !conProblema[item.articuloId],
          observacion: conProblema[item.articuloId] ? (notasProblema[item.articuloId] ?? '').trim().slice(0, 500) : null,
        })),
      })
      setRegistrada(creada)
      refrescarAvisos()
    } catch (err) {
      setErrorApi(mensajeDeErrorApi(err))
      setRevisando(false)
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  // ── Entrega con problema registrada (#224) ──────────────────────────────────
  if (problemaRegistrado && elegida) {
    return (
      <section className={styles.contenedor}>
        <div className={styles.confirmacion} role="status">
          <h1 className={styles.tituloExito}>Problema registrado</h1>
          <p className={styles.dato}>La orden sigue esperando la mercadería: no cambió lo que ya recibiste.</p>
          {problemaRegistrado.motivoProblema && (
            <p className={styles.dato}>
              <strong>Motivo:</strong> {problemaRegistrado.motivoProblema}
            </p>
          )}
          <p className={styles.dato}>
            <strong>Obra:</strong> {nombreDeObra(elegida.obraId)}
          </p>
          {(problemaRegistrado.fotos ?? []).length > 0 && (
            <p className={styles.dato}>
              <strong>Fotos enviadas:</strong> {problemaRegistrado.fotos?.length}
            </p>
          )}
          <div className={styles.accionesExito}>
            <Link to="historial" className={styles.enlaceBoton}>
              Ver historial de recepciones
            </Link>
            <button type="button" className={styles.boton} onClick={recibirOtra}>
              Volver a las órdenes
            </button>
            <Link to="/" className={styles.enlaceBoton}>
              Volver al inicio
            </Link>
          </div>
        </div>
      </section>
    )
  }

  // ── Formulario del problema (#224) ──────────────────────────────────────────
  if (reportandoProblema && elegida) {
    return (
      <section className={styles.contenedor}>
        <button type="button" className={styles.volver} onClick={() => setReportandoProblema(false)}>
          ← Volver a la recepción
        </button>
        <h1>Hubo un problema con la entrega</h1>
        <p className={styles.obra}>Obra: {nombreDeObra(elegida.obraId)}</p>
        <EntregaConProblemaForm
          orden={elegida}
          onRegistrada={(creada) => {
            setReportandoProblema(false)
            setProblemaRegistrado(creada)
          }}
          onCancelar={() => setReportandoProblema(false)}
        />
      </section>
    )
  }

  // ── Pantalla de confirmación ────────────────────────────────────────────────
  if (registrada && elegida) {
    return (
      <section className={styles.contenedor}>
        <div className={styles.confirmacion} role="status">
          <h1 className={styles.tituloExito}>Recepción registrada</h1>
          <p className={styles.dato}>
            {registrada.estado === 3
              ? 'La orden quedó recibida por completo.'
              : registrada.estado === 2
                ? 'Recepción parcial: todavía queda mercadería pendiente.'
                : 'Se registró la recepción.'}
          </p>
          <p className={styles.dato}>
            {registrada.estado === 3
              ? 'El seguimiento de esta entrega terminó.'
              : 'Volvé a las órdenes pendientes para registrar la próxima entrega cuando llegue.'}
          </p>
          <p className={styles.dato}>
            <strong>Remito:</strong> {registrada.numeroRemito}
          </p>
          <p className={styles.dato}>
            <strong>Obra:</strong> {nombreDeObra(elegida.obraId)}
          </p>
          <ul className={styles.items}>
            {elegida.items.map((item) => (
              <li key={item.articuloId}>
                {cantidadConUnidad(Number(cantidades[item.articuloId] ?? 0), item.unidadMedida)} ·{' '}
                {item.descripcionArticulo}
              </li>
            ))}
          </ul>
          <div className={styles.accionesExito}>
            <Link to="historial" className={styles.enlaceBoton}>
              Ver historial de recepciones
            </Link>
            <button type="button" className={styles.boton} onClick={recibirOtra}>
              Recibir otra orden
            </button>
            <Link to="/" className={styles.enlaceBoton}>
              Volver al inicio
            </Link>
          </div>
        </div>
      </section>
    )
  }

  // ── Formulario del remito ───────────────────────────────────────────────────
  if (elegida && revisando) {
    return (
      <section className={styles.contenedor} aria-labelledby="titulo-revision-recepcion">
        <h1 id="titulo-revision-recepcion">Revisá la recepción</h1>
        <div className={styles.tarjeta}>
          <p className={styles.dato}><strong>Obra:</strong> {nombreDeObra(elegida.obraId)}</p>
          <p className={styles.dato}><strong>Remito:</strong> {numeroRemito.trim()}</p>
          <p className={styles.dato}><strong>Fecha:</strong> {fechaEnPalabras(fecha)}</p>
          <ul className={styles.items}>
            {elegida.items.map((item) => (
              <li key={item.articuloId}>
                {item.descripcionArticulo}: {cantidadConUnidad(Number(cantidades[item.articuloId]), item.unidadMedida)}
                {conProblema[item.articuloId] && <p>{notasProblema[item.articuloId]}</p>}
              </li>
            ))}
          </ul>
          {observaciones.trim() && <p className={styles.dato}><strong>Observaciones:</strong> {observaciones.trim()}</p>}
        </div>
        <div className={styles.accionesExito}>
          <button type="button" className={styles.volver} disabled={enviando} onClick={() => setRevisando(false)}>
            Editar
          </button>
          <button type="button" className={styles.boton} disabled={enviando} onClick={confirmarRecepcion}>
            {enviando ? 'Guardando…' : 'Confirmar recepción'}
          </button>
        </div>
      </section>
    )
  }

  if (elegida) {
    return (
      <section className={styles.contenedor}>
        <button
          type="button"
          className={styles.volver}
          onClick={() => {
            descartarLecturaDeFoto()
            setElegida(null)
          }}
        >
          ← Elegir otra orden
        </button>
        <h1>Recibir mercadería</h1>
        <p className={styles.obra}>Obra: {nombreDeObra(elegida.obraId)}</p>

        <section className={styles.tarjeta} aria-label="Materiales de la orden">
          <p className={styles.subtitulo}>Materiales y saldo pendiente:</p>
          <ul className={styles.items}>
            {elegida.items.map((item) => (
              <li key={item.articuloId}>
                {item.descripcionArticulo}: Pedido {cantidadConUnidad(item.cantidadEsperada, item.unidadMedida)} ·
                Recibido {cantidadConUnidad(item.cantidadRecibida, item.unidadMedida)} ·
                Pendiente {cantidadConUnidad(saldoPendiente(item), item.unidadMedida)}
              </li>
            ))}
          </ul>
        </section>

        <section className={styles.tarjeta} aria-label="Historial de recepciones">
          <p className={styles.subtitulo}>Entregas anteriores</p>
          {cargandoHistorial && <p role="status">Cargando historial…</p>}
          {errorHistorial && <p role="alert">No se pudo cargar el historial. Podés reintentar eligiendo la orden nuevamente.</p>}
          {!cargandoHistorial && !errorHistorial && historial.length === 0 && <p className={styles.dato}>Todavía no hay entregas registradas.</p>}
          {/* Línea de tiempo (#224): el despacho del Proveedor, las entregas parciales, los problemas con sus fotos y la recepción completa. */}
          <LineaDeTiempoOrden
            despacho={{
              fechaDespacho: elegida.fechaDespacho,
              fechaEstimadaLlegada: elegida.fechaEstimadaLlegada,
              observacionDespacho: elegida.observacionDespacho,
            }}
            recepciones={historial}
          />
        </section>

        {elegida.tieneProblemaAbierto && (
          <p className={styles.avisoProblema} role="status">
            Esta orden tiene un problema de entrega abierto. Se cierra cuando registres una recepción válida.
          </p>
        )}
        <div className={styles.tarjeta}>
          <p className={styles.subtitulo}>¿No llegó como esperabas?</p>
          <p className={styles.ayuda}>Si no llegó nada, llegó incompleta, llegó dañada o no es lo pedido, avisá desde acá.</p>
          <button type="button" className={styles.botonProblema} onClick={() => setReportandoProblema(true)}>
            Hubo un problema con la entrega
          </button>
        </div>

        <form
          className={styles.formulario}
          onSubmit={revisarRecepcion}
          noValidate
          aria-describedby={errorApi ? 'recepcion-error-api' : undefined}
        >
          {errorApi && (
            <p id="recepcion-error-api" className={styles.errorApi} role="alert">
              {errorApi}
            </p>
          )}
          {erroresCantidad.general && <p className={styles.errorApi} role="alert">{erroresCantidad.general}</p>}
          {elegida.items.map((item) => (
            <div className={styles.grupo} key={item.articuloId}>
              <label htmlFor={`cantidad-${item.articuloId}`} className={styles.label}>{item.descripcionArticulo} · {item.unidadMedida} — recibido en esta entrega</label>
              <p className={styles.ayuda}>Pedido: {item.cantidadEsperada} · Ya recibidas: {item.cantidadRecibida} · Saldo: {saldoPendiente(item)}</p>
              <input id={`cantidad-${item.articuloId}`} className={`${styles.input}${erroresCantidad[item.articuloId] ? ` ${styles.inputInvalido}` : ''}`} type="number" min="0" max={saldoPendiente(item)} step="any" inputMode="decimal" value={cantidades[item.articuloId] ?? ''} onChange={(e) => { setCantidades((prev) => ({ ...prev, [item.articuloId]: e.target.value })); setErroresCantidad((prev) => ({ ...prev, [item.articuloId]: undefined, general: undefined })) }} aria-invalid={!!erroresCantidad[item.articuloId]} aria-describedby={erroresCantidad[item.articuloId] ? `cantidad-error-${item.articuloId}` : undefined} />
              <label className={styles.checkProblema}>
                <input
                  type="checkbox"
                  checked={!!conProblema[item.articuloId]}
                  onChange={(e) => {
                    setConProblema((prev) => ({ ...prev, [item.articuloId]: e.target.checked }))
                    setErroresCantidad((prev) => ({ ...prev, [item.articuloId]: undefined }))
                  }}
                />
                Llegó dañado o con diferencias
              </label>
              {conProblema[item.articuloId] && (
                <input
                  type="text"
                  className={styles.input}
                  aria-label={`Qué pasó con ${item.descripcionArticulo}`}
                  placeholder="Ej: 3 bolsas rotas, llegó mojado…"
                  maxLength={500}
                  value={notasProblema[item.articuloId] ?? ''}
                  onChange={(e) => {
                    setNotasProblema((prev) => ({ ...prev, [item.articuloId]: e.target.value }))
                    setErroresCantidad((prev) => ({ ...prev, [item.articuloId]: undefined }))
                  }}
                />
              )}
              {erroresCantidad[item.articuloId] && <p id={`cantidad-error-${item.articuloId}`} className={styles.errorCampo} role="alert">{erroresCantidad[item.articuloId]}</p>}
            </div>
          ))}

          {/* Foto del remito (#209): opcional. Carga el número y la fecha como sugerencia; la persona los revisa. */}
          <div className={styles.fotoRemito}>
            <input
              ref={entradaFotoRef}
              type="file"
              accept="image/*"
              capture="environment"
              hidden
              aria-label="Foto del remito"
              onChange={(e) => void handleFoto(e)}
            />
            <button
              type="button"
              className={styles.botonFoto}
              onClick={() => entradaFotoRef.current?.click()}
              disabled={foto.tipo === 'leyendo'}
              aria-busy={foto.tipo === 'leyendo'}
            >
              {foto.tipo === 'leyendo' ? 'Leyendo el remito…' : 'Sacar foto del remito'}
            </button>
            <p className={styles.ayuda}>Opcional: leo el número y la fecha por vos. Revisalos antes de confirmar.</p>
            {foto.tipo === 'listo' && (
              <p className={styles.estadoFoto} role="status">
                {foto.texto}
              </p>
            )}
            {foto.tipo === 'error' && (
              <p className={styles.errorApi} role="alert">
                {foto.texto}
              </p>
            )}
          </div>

          <div className={styles.grupo}>
            <label htmlFor="recepcion-remito" className={styles.label}>
              Número de remito
            </label>
            <input
              id="recepcion-remito"
              type="text"
              className={`${styles.input}${errores.numeroRemito ? ` ${styles.inputInvalido}` : ''}`}
              value={numeroRemito}
              onChange={(e) => setNumeroRemito(e.target.value)}
              maxLength={LARGO_MAXIMO_REMITO}
              autoComplete="off"
              aria-invalid={!!errores.numeroRemito}
              aria-describedby={errores.numeroRemito ? 'recepcion-remito-error' : undefined}
            />
            {errores.numeroRemito && (
              <p id="recepcion-remito-error" className={styles.errorCampo} role="alert">
                {errores.numeroRemito}
              </p>
            )}
          </div>

          <div className={styles.grupo}>
            <label htmlFor="recepcion-fecha" className={styles.label}>
              Fecha de recepción
            </label>
            <input
              id="recepcion-fecha"
              type="date"
              // Tope en "hoy" (#127): no se puede recibir en el futuro. El backend valida igual.
              max={hoyComoTexto()}
              className={`${styles.input}${errores.fecha ? ` ${styles.inputInvalido}` : ''}`}
              value={fecha}
              onChange={(e) => setFecha(e.target.value)}
              onClick={(e) => {
                // Toda la caja abre el calendario, no solo el iconito.
                try {
                  e.currentTarget.showPicker?.()
                } catch {
                  /* El navegador no lo permite en este momento: queda el uso normal. */
                }
              }}
              aria-invalid={!!errores.fecha}
              aria-describedby={errores.fecha ? 'recepcion-fecha-error' : undefined}
            />
            {fechaEnPalabras(fecha) && (
              <p className={styles.ayuda}>Llegó el {fechaEnPalabras(fecha)}.</p>
            )}
            {esFechaPasada(fecha) && (
              <p className={styles.ayuda}>Es una fecha anterior a hoy. Revisá que sea la del remito.</p>
            )}
            {errores.fecha && (
              <p id="recepcion-fecha-error" className={styles.errorCampo} role="alert">
                {errores.fecha}
              </p>
            )}
          </div>

          <div className={styles.grupo}>
            <label htmlFor="recepcion-observaciones" className={styles.label}>
              Observaciones (opcional)
            </label>
            <textarea
              id="recepcion-observaciones"
              className={`${styles.input} ${styles.textarea}${errores.observaciones ? ` ${styles.inputInvalido}` : ''}`}
              value={observaciones}
              onChange={(e) => setObservaciones(e.target.value)}
              maxLength={LARGO_MAXIMO_OBSERVACIONES}
              aria-invalid={!!errores.observaciones}
              aria-describedby={errores.observaciones ? 'recepcion-observaciones-error' : undefined}
            />
            {errores.observaciones && (
              <p id="recepcion-observaciones-error" className={styles.errorCampo} role="alert">
                {errores.observaciones}
              </p>
            )}
          </div>

          <button
            type="submit"
            className={styles.boton}
            disabled={enviando || foto.tipo === 'leyendo'}
            aria-busy={enviando}
          >
            Revisar recepción
          </button>
        </form>
      </section>
    )
  }

  // ── Lista de órdenes pendientes ─────────────────────────────────────────────
  return (
    <section className={styles.contenedor}>
      <Link to="/" className={styles.volverEnlace}>
        ← Volver al inicio
      </Link>
      <h1>Recibir mercadería</h1>
      <Link to="historial" className={styles.enlaceBoton}>
        Ver historial de recepciones
      </Link>
      <p className="intro">
        Elegí la orden que llegó a la obra. Acá solo aparecen las órdenes que el Proveedor ya despachó.
      </p>

      {cargando && <p role="status">Cargando…</p>}

      {!cargando && errorCarga && (
        <div>
          <p className={styles.errorApi} role="alert">
            {errorCarga}
          </p>
          <button type="button" className={styles.boton} onClick={recargar}>
            Reintentar
          </button>
        </div>
      )}

      {!cargando && !errorCarga && ordenes.length === 0 && (
        <div className={styles.tarjeta} role="status">
          <p className={styles.subtitulo}>No hay órdenes despachadas pendientes de recibir.</p>
          <p className={styles.dato}>Cuando el Proveedor despache una orden, la vas a ver acá.</p>
        </div>
      )}

      {!cargando && !errorCarga && ordenes.length > 0 && (
        <ul className={styles.lista}>
          {ordenes.map((orden) => {
            const tieneRecepciones = orden.items.some((item) => item.cantidadRecibida > 0)
            return (
              <li key={orden.ordenCompraId}>
                <button type="button" className={styles.orden} onClick={() => elegirOrden(orden)}>
                  <span className={styles.ordenObra}>{nombreDeObra(orden.obraId)}</span>
                  {orden.etapa && <span className={styles.ordenItem}>Etapa: {orden.etapa}</span>}
                  {orden.fechaEstimadaLlegada && <span className={styles.ordenItem}>Llega el {fechaLarga(orden.fechaEstimadaLlegada)}</span>}
                  {orden.observacionDespacho && <span className={styles.ordenItem}>Observación del Proveedor: {orden.observacionDespacho}</span>}
                  {orden.tieneProblemaAbierto && <span className={styles.ordenProblema}>Problema de entrega abierto</span>}
                  {orden.estadoEntrega === ESTADO_ENTREGA_PARCIAL ? (
                    <span className={styles.ordenItem}>{ESTADO_ENTREGA_PARCIAL}: el Proveedor tiene que despachar el resto</span>
                  ) : (
                    tieneRecepciones && <span className={styles.ordenItem}>Recepción parcial: quedan materiales pendientes</span>
                  )}
                  {orden.items.map((item) => {
                    const recibida = item.cantidadRecibida ?? 0
                    const pendiente = saldoPendiente(item)
                    return (
                    <span key={item.articuloId} className={styles.ordenItem}>
                      {item.descripcionArticulo}: Pedido {cantidadConUnidad(item.cantidadEsperada, item.unidadMedida)} ·
                      Recibido {cantidadConUnidad(recibida, item.unidadMedida)} ·
                      Pendiente {cantidadConUnidad(pendiente, item.unidadMedida)}
                    </span>
                  )})}
                </button>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
