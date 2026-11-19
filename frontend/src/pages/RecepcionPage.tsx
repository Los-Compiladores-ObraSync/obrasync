import { type ChangeEvent, type FormEvent, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { ApiError } from '../api/ApiError'
import { esFechaFutura, esFechaPasada, fechaEnPalabras, hoyComoTexto } from '../obras/fechaEnPalabras'
import { fechaAIso } from '../obras/validacionObra'
import { ErrorDeImagen, reducirImagen } from '../recepciones/imagenRemito'
import { leerRemitoDeFoto, obtenerOrdenesPendientes, registrarRecepcion } from '../recepciones/recepcionesService'
import type { OrdenPendiente, RecepcionRegistrada } from '../recepciones/recepcionesTipos'
import {
  hayErrores,
  LARGO_MAXIMO_OBSERVACIONES,
  LARGO_MAXIMO_REMITO,
  validarRecepcion,
  type ErroresRecepcion,
} from '../recepciones/validacionRecepcion'
import { obtenerObras } from '../requisiciones/requisicionesService'
import { cantidadConUnidad } from '../requisiciones/unidades'
import styles from './RecepcionPage.module.css'

// Cuántos materiales se ven en cada tarjeta de la lista (el resto se resume en "y N más").
const ITEMS_VISIBLES_EN_TARJETA = 3

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

/** Recibir mercadería en obra (#82): el Capataz elige la orden de compra que llegó y carga el
 *  remito. Por ahora solo se registra la recepción TOTAL: el contrato rechaza las entregas
 *  parciales hasta el Sprint 3 (#90), así que las cantidades se muestran y no se editan. */
export function RecepcionPage() {
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
  const [errores, setErrores] = useState<ErroresRecepcion>({})
  const [errorApi, setErrorApi] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  // Guard inmediato contra el doble envío (mismo criterio que #109): el estado `enviando` recién
  // cambia en el próximo render y un segundo toque podría colarse antes.
  const enviandoRef = useRef(false)
  const [registrada, setRegistrada] = useState<RecepcionRegistrada | null>(null)

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
    setElegida(orden)
    setNumeroRemito('')
    setFecha(hoyComoTexto())
    setObservaciones('')
    setErrores({})
    setErrorApi(null)
  }

  function recibirOtra() {
    descartarLecturaDeFoto()
    setRegistrada(null)
    setElegida(null)
    recargar()
  }

  function nombreDeObra(obraId: string): string {
    return nombresObras[obraId] ?? 'Obra'
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (enviandoRef.current || !elegida) return

    // 1. Validación local: no llama a la API si hay errores de campo.
    const erroresForm = validarRecepcion({ numeroRemito, fecha, observaciones })
    if (hayErrores(erroresForm)) {
      setErrores(erroresForm)
      return
    }
    setErrores({})
    setErrorApi(null)
    enviandoRef.current = true
    setEnviando(true)

    try {
      const creada = await registrarRecepcion({
        obraId: elegida.obraId,
        ordenCompraId: elegida.ordenCompraId,
        numeroRemito: numeroRemito.trim(),
        fechaRecepcion: fechaAIso(fecha),
        observaciones: observaciones.trim() || null,
        // Recepción total: llegó lo comprado y está conforme (el contrato rechaza lo demás).
        detalles: elegida.items.map((item) => ({
          articuloId: item.articuloId,
          descripcionArticulo: item.descripcionArticulo,
          cantidadEsperada: item.cantidadEsperada,
          cantidadRecibida: item.cantidadEsperada,
          unidadMedida: item.unidadMedida,
          conforme: true,
        })),
      })
      setRegistrada(creada)
    } catch (err) {
      setErrorApi(mensajeDeErrorApi(err))
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  // ── Pantalla de confirmación ────────────────────────────────────────────────
  if (registrada && elegida) {
    return (
      <section className={styles.contenedor}>
        <div className={styles.confirmacion} role="status">
          <h1 className={styles.tituloExito}>Recepción registrada</h1>
          <p className={styles.dato}>
            <strong>Remito:</strong> {registrada.numeroRemito}
          </p>
          <p className={styles.dato}>
            <strong>Obra:</strong> {nombreDeObra(elegida.obraId)}
          </p>
          <ul className={styles.items}>
            {elegida.items.map((item) => (
              <li key={item.articuloId}>
                {cantidadConUnidad(item.cantidadEsperada, item.unidadMedida)} ·{' '}
                {item.descripcionArticulo}
              </li>
            ))}
          </ul>
          <div className={styles.accionesExito}>
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
          <p className={styles.subtitulo}>Tiene que haber llegado:</p>
          <ul className={styles.items}>
            {elegida.items.map((item) => (
              <li key={item.articuloId}>
                {cantidadConUnidad(item.cantidadEsperada, item.unidadMedida)} ·{' '}
                {item.descripcionArticulo}
              </li>
            ))}
          </ul>
        </section>

        <form
          className={styles.formulario}
          onSubmit={handleSubmit}
          noValidate
          aria-describedby={errorApi ? 'recepcion-error-api' : undefined}
        >
          {errorApi && (
            <p id="recepcion-error-api" className={styles.errorApi} role="alert">
              {errorApi}
            </p>
          )}

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
            {enviando ? 'Guardando…' : 'Llegó todo: confirmar recepción'}
          </button>
          <p className={styles.nota}>
            ¿No llegó todo? Todavía no se puede registrar una entrega parcial: avisale a Compras.
          </p>
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
      <p className="intro">
        Elegí la orden que llegó a la obra. Acá solo aparecen las órdenes que Compras ya despachó.
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
          <p className={styles.dato}>Cuando Compras despache una orden, la vas a ver acá.</p>
        </div>
      )}

      {!cargando && !errorCarga && ordenes.length > 0 && (
        <ul className={styles.lista}>
          {ordenes.map((orden) => {
            const visibles = orden.items.slice(0, ITEMS_VISIBLES_EN_TARJETA)
            const restantes = orden.items.length - visibles.length
            return (
              <li key={orden.ordenCompraId}>
                <button type="button" className={styles.orden} onClick={() => elegirOrden(orden)}>
                  <span className={styles.ordenObra}>{nombreDeObra(orden.obraId)}</span>
                  {visibles.map((item) => (
                    <span key={item.articuloId} className={styles.ordenItem}>
                      {cantidadConUnidad(item.cantidadEsperada, item.unidadMedida)} ·{' '}
                      {item.descripcionArticulo}
                    </span>
                  ))}
                  {restantes > 0 && (
                    <span className={styles.ordenItem}>y {restantes} más</span>
                  )}
                </button>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
