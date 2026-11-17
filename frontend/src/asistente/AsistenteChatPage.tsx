import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { Link } from 'react-router'
import { ApiError } from '../api/ApiError'
import {
  aprobarRequisicion,
  crearRequisicion,
  generarIdempotencyKey,
  rechazarRequisicion,
} from '../requisiciones/requisicionesService'
import { fechaAIso } from '../obras/validacionObra'
import { hoyComoTexto } from '../obras/fechaEnPalabras'
import { registrarRecepcion } from '../recepciones/recepcionesService'
import { enviarMensajeAlAsistente } from './asistenteService'
import {
  MAX_CARACTERES_MENSAJE,
  type BorradorAprobacionDto,
  type BorradorRecepcionDto,
  type BorradorRequisicionDto,
  type ChatMensajeDto,
} from './asistenteTipos'
import styles from './AsistenteChatPage.module.css'
import { ConfirmacionAccion } from './ConfirmacionAccion'

type EstadoBorrador = 'pendiente' | 'confirmada' | 'cancelada'

interface MensajeChat {
  id: number
  rol: 'usuario' | 'asistente'
  texto: string
  /** Mensaje armado por la propia pantalla (no lo dijo el modelo): no se reenvía como historial. */
  local?: boolean
  /** Muestra "qué querés hacer ahora" debajo del mensaje (después de confirmar o cancelar un borrador). */
  siguientesPasos?: 'creada' | 'descartada' | 'aprobada' | 'rechazada' | 'recibida'
  borrador?: BorradorRequisicionDto
  /** Borrador de aprobar o rechazar una requisición (portal de Compras, #141 y #149). */
  aprobacion?: BorradorAprobacionDto
  /** Borrador de recepción total (portal de obra, #147). */
  recepcion?: BorradorRecepcionDto
  /** Una sola clave por borrador: si el usuario confirma dos veces, el servidor no duplica la requisición. */
  claveIdempotencia?: string
  estado?: EstadoBorrador
}

/** Qué portal está usando el asistente: cambia lo que ofrece y a dónde lleva los enlaces. */
export type PortalAsistente = 'obra' | 'compras'

/** Lo que el asistente sabe hacer, para que el Capataz lo descubra. Lo que todavía no anda se muestra con la
 *  etiqueta "Próximamente": cuando se sume una función, se cambia su `disponible` a true (la consulta de
 *  requisiciones ya anda; la recepción por chat es lo que falta de la tarjeta #147). */
const CAPACIDADES_OBRA = [
  {
    titulo: 'Pedir materiales',
    descripcion: 'Contame qué necesitás y armo la requisición. Vos la revisás y la confirmás.',
    mensaje: 'Quiero pedir materiales',
    disponible: true,
  },
  {
    titulo: 'Ver tus requisiciones',
    descripcion: 'Preguntame en qué quedó una requisición tuya.',
    mensaje: 'Quiero ver mis requisiciones',
    disponible: true, // #147
  },
  {
    titulo: 'Registrar lo que llegó',
    descripcion: 'Te armo la recepción de la mercadería para que la confirmes.',
    mensaje: 'Quiero registrar lo que llegó',
    disponible: true, // #147 (solo recepción total; la parcial se hace desde la pantalla de Recepción)
  },
]

/** Lo que el asistente sabe hacer en el portal de Compras (#141 y #149): consultar, aprobar y rechazar con confirmación. */
const CAPACIDADES_COMPRAS = [
  {
    titulo: 'Ver requisiciones pendientes',
    descripcion: 'Te cuento cuáles están esperando tu aprobación.',
    mensaje: '¿Qué requisiciones están pendientes?',
    disponible: true,
  },
  {
    titulo: 'Aprobar una requisición',
    descripcion: 'Te armo la aprobación para que la revises y la confirmes.',
    mensaje: 'Quiero aprobar una requisición',
    disponible: true,
  },
  {
    titulo: 'Rechazar una requisición',
    descripcion: 'Te armo el rechazo con tu motivo para que lo revises y lo confirmes.',
    mensaje: 'Quiero rechazar una requisición',
    disponible: true,
  },
]

/** A partir de cuántos caracteres se muestra el contador (antes sería ruido). */
const CARACTERES_PARA_MOSTRAR_CONTADOR = 400

function mensajeDeErrorDelAsistente(err: unknown, portal: PortalAsistente): string {
  if (err instanceof ApiError) {
    if (err.status === 400) {
      return err.detail ?? 'No pude entender el mensaje. Probá escribirlo de nuevo.'
    }
    if (err.status === 403) {
      return 'Tu usuario no puede usar el asistente.'
    }
    if (err.status === 429) {
      return 'El asistente está ocupado. Probá de nuevo en un minuto.'
    }
    if (err.status === 503) {
      return portal === 'compras'
        ? 'El asistente no está disponible ahora. Podés aprobar o rechazar desde la pantalla de requisiciones.'
        : 'El asistente no está disponible ahora. Podés cargar la requisición con el formulario.'
    }
  }
  return portal === 'compras'
    ? 'No se pudo conectar con el asistente. Probá de nuevo o usá la pantalla de requisiciones.'
    : 'No se pudo conectar con el asistente. Probá de nuevo o usá el formulario.'
}

function formatoCantidad(cantidad: number): string {
  return new Intl.NumberFormat('es-AR', { maximumFractionDigits: 4 }).format(cantidad)
}

function IconoDestello() {
  return (
    <svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor" focusable="false" aria-hidden="true">
      <path d="M9 3l1.8 4.7L15.5 9.5l-4.7 1.8L9 16l-1.8-4.7L2.5 9.5l4.7-1.8L9 3z" />
      <path d="M18 12l1.1 2.9 2.9 1.1-2.9 1.1L18 20l-1.1-2.9-2.9-1.1 2.9-1.1L18 12z" />
    </svg>
  )
}

/** Chat del asistente en el portal del Capataz (tarjeta #138) y, con portal="compras", en el del Comprador (#141).
 *  - Todo texto (del usuario o del asistente) se muestra como TEXTO, nunca como HTML (#174).
 *  - El asistente nunca crea ni aprueba nada: arma un borrador y solo el botón Confirmar (#146) llama al
 *    endpoint de requisiciones con el JWT del usuario. */
export function AsistenteChatPage({ portal = 'obra' }: { portal?: PortalAsistente } = {}) {
  const capacidades = portal === 'compras' ? CAPACIDADES_COMPRAS : CAPACIDADES_OBRA
  const [mensajes, setMensajes] = useState<MensajeChat[]>([])
  const [texto, setTexto] = useState('')
  const [pensando, setPensando] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const siguienteId = useRef(0)
  const finDeLista = useRef<HTMLDivElement>(null)
  const caja = useRef<HTMLTextAreaElement>(null)
  // Evita que dos envíos seguidos salgan antes del siguiente render.
  const enviandoRef = useRef(false)

  useEffect(() => {
    finDeLista.current?.scrollIntoView?.({ block: 'end' })
  }, [mensajes, pensando])

  // La caja de texto crece con lo que se escribe (hasta un máximo que fija el CSS).
  useEffect(() => {
    const elemento = caja.current
    if (elemento && elemento.scrollHeight > 0) {
      elemento.style.height = 'auto'
      elemento.style.height = `${elemento.scrollHeight}px`
    }
  }, [texto])

  function actualizarMensaje(id: number, cambios: Partial<MensajeChat>) {
    setMensajes((actuales) => actuales.map((m) => (m.id === id ? { ...m, ...cambios } : m)))
  }

  function agregarMensaje(mensaje: Omit<MensajeChat, 'id'>): number {
    const id = siguienteId.current++
    setMensajes((actuales) => [...actuales, { ...mensaje, id }])
    return id
  }

  function enviar(evento: FormEvent) {
    evento.preventDefault()
    void enviarTexto(texto)
  }

  /** Enter envía el mensaje; Shift+Enter baja de renglón. No se envía mientras un teclado compone un carácter (acentos, IME). */
  function alApretarTecla(evento: KeyboardEvent<HTMLTextAreaElement>) {
    if (evento.key === 'Enter' && !evento.shiftKey && !evento.nativeEvent.isComposing) {
      evento.preventDefault()
      void enviarTexto(texto)
    }
  }

  async function enviarTexto(textoAEnviar: string) {
    const mensaje = textoAEnviar.trim()
    if (!mensaje || enviandoRef.current) {
      return
    }
    enviandoRef.current = true
    const historial: ChatMensajeDto[] = mensajes
      .filter((m) => !m.local)
      .map((m) => ({ rol: m.rol, texto: m.texto }))

    setError(null)
    setTexto('')
    agregarMensaje({ rol: 'usuario', texto: mensaje })
    setPensando(true)
    try {
      const respuesta = await enviarMensajeAlAsistente(mensaje, historial)
      agregarMensaje({
        rol: 'asistente',
        texto: respuesta.respuesta,
        borrador: respuesta.borrador ?? undefined,
        aprobacion: respuesta.aprobacion ?? undefined,
        recepcion: respuesta.recepcion ?? undefined,
        claveIdempotencia: respuesta.borrador ? generarIdempotencyKey() : undefined,
        estado: respuesta.borrador || respuesta.aprobacion || respuesta.recepcion ? 'pendiente' : undefined,
      })
    } catch (err) {
      setError(mensajeDeErrorDelAsistente(err, portal))
    } finally {
      setPensando(false)
      enviandoRef.current = false
    }
  }

  async function confirmarBorrador(mensaje: MensajeChat) {
    if (!mensaje.borrador) {
      return
    }
    // Si falla, el error lo muestra la tarjeta y el borrador sigue pendiente para reintentar.
    const creada = await crearRequisicion(mensaje.borrador.requisicion, mensaje.claveIdempotencia)
    actualizarMensaje(mensaje.id, { estado: 'confirmada' })
    agregarMensaje({
      rol: 'asistente',
      texto: `Listo, creé la requisición ${creada.numeroRequisicion}. Quedó pendiente de aprobación de Compras.`,
      local: true,
      siguientesPasos: 'creada',
    })
  }

  async function confirmarAprobacion(mensaje: MensajeChat) {
    const borrador = mensaje.aprobacion
    if (!borrador) {
      return
    }
    // Si falla (por ejemplo un 409 porque ya la resolvió otra persona), el error lo muestra la tarjeta y se puede reintentar o cancelar.
    if (borrador.accion === 'rechazarRequisicion') {
      // El motivo es el mismo que la persona vio en la tarjeta. Si faltara, el servidor responde 400 y la tarjeta lo explica.
      const rechazada = await rechazarRequisicion(borrador.requisicionId, borrador.motivo ?? '')
      actualizarMensaje(mensaje.id, { estado: 'confirmada' })
      agregarMensaje({
        rol: 'asistente',
        texto: `Listo, rechacé la requisición ${rechazada.numeroRequisicion ?? borrador.resumen.numero}.`,
        local: true,
        siguientesPasos: 'rechazada',
      })
      return
    }
    const aprobada = await aprobarRequisicion(borrador.requisicionId)
    actualizarMensaje(mensaje.id, { estado: 'confirmada' })
    agregarMensaje({
      rol: 'asistente',
      texto: `Listo, aprobé la requisición ${aprobada.numeroRequisicion ?? borrador.resumen.numero}.`,
      local: true,
      siguientesPasos: 'aprobada',
    })
  }

  async function confirmarRecepcion(mensaje: MensajeChat) {
    if (!mensaje.recepcion) {
      return
    }
    // La fecha la pone el frontend al confirmar (igual que la pantalla de Recepción: hoy, hora local).
    // Si falla (409: orden ya recibida o remito repetido; 400: entrega parcial), el error lo muestra la tarjeta.
    await registrarRecepcion({
      ...mensaje.recepcion.recepcion,
      fechaRecepcion: fechaAIso(hoyComoTexto()),
    })
    actualizarMensaje(mensaje.id, { estado: 'confirmada' })
    agregarMensaje({
      rol: 'asistente',
      texto: `Listo, registré la recepción del remito ${mensaje.recepcion.resumen.numeroRemito}.`,
      local: true,
      siguientesPasos: 'recibida',
    })
  }

  function cancelarRecepcion(mensaje: MensajeChat) {
    actualizarMensaje(mensaje.id, { estado: 'cancelada' })
    agregarMensaje({
      rol: 'asistente',
      texto: 'Listo, no registré nada. La orden sigue pendiente de recepción.',
      local: true,
      siguientesPasos: 'descartada',
    })
  }

  function cancelarAprobacion(mensaje: MensajeChat) {
    actualizarMensaje(mensaje.id, { estado: 'cancelada' })
    agregarMensaje({
      rol: 'asistente',
      texto:
        mensaje.aprobacion?.accion === 'rechazarRequisicion'
          ? 'Listo, no rechacé nada. La requisición sigue pendiente.'
          : 'Listo, no aprobé nada. La requisición sigue pendiente.',
      local: true,
      siguientesPasos: 'descartada',
    })
  }

  function cancelarBorrador(mensaje: MensajeChat) {
    actualizarMensaje(mensaje.id, { estado: 'cancelada' })
    agregarMensaje({
      rol: 'asistente',
      texto: 'Listo, descarté el borrador. No se creó nada.',
      local: true,
      siguientesPasos: 'descartada',
    })
  }

  // Solo el último mensaje ofrece "qué hacer ahora": los anteriores ya quedaron atrás en la conversación.
  const ultimoId = mensajes.length > 0 ? mensajes[mensajes.length - 1].id : -1
  const vacio = texto.trim().length === 0
  const conversacionNueva = mensajes.length === 0

  return (
    <section className={styles.contenedor}>
      <header className={styles.encabezado}>
        <Link to="/" className={styles.volver} aria-label="Volver al inicio">
          <span aria-hidden="true">←</span>
        </Link>
        <h1 className={styles.titulo}>Asistente</h1>
      </header>

      <div className={styles.conversacion} role="log" aria-live="polite" aria-label="Conversación">
        {conversacionNueva && (
          <div className={styles.inicio}>
            <span className={styles.iconoGrande} aria-hidden="true">
              <IconoDestello />
            </span>
            <p className={styles.pregunta}>¿En qué te ayudo?</p>
            <ul className={styles.capacidades} aria-label="Lo que puedo hacer">
              {capacidades.map((capacidad) => (
                <li key={capacidad.titulo}>
                  {/* Tocar una capacidad manda ese mensaje: el asistente responde con lo que necesita. */}
                  <button
                    type="button"
                    className={styles.capacidad}
                    disabled={!capacidad.disponible || pensando}
                    onClick={() => void enviarTexto(capacidad.mensaje)}
                  >
                    <span className={styles.capacidadTitulo}>
                      {capacidad.titulo}
                      {!capacidad.disponible && <span className={styles.pronto}>Próximamente</span>}
                    </span>
                    <span className={styles.capacidadDescripcion}>{capacidad.descripcion}</span>
                  </button>
                </li>
              ))}
            </ul>
          </div>
        )}

        {mensajes.map((mensaje) =>
          mensaje.rol === 'usuario' ? (
            <div key={mensaje.id} className={styles.delUsuario}>
              <p className={styles.texto}>{mensaje.texto}</p>
            </div>
          ) : (
            <div key={mensaje.id} className={styles.delAsistente}>
              <span className={styles.marca} aria-hidden="true">
                <IconoDestello />
              </span>
              <div className={styles.contenido}>
                <p className={styles.texto}>{mensaje.texto}</p>
                {mensaje.siguientesPasos && mensaje.id === ultimoId && !pensando && (
                  <div className={styles.siguientesPasos} role="group" aria-label="¿Qué querés hacer ahora?">
                    {portal === 'compras' ? (
                      <button
                        type="button"
                        className={styles.siguientePaso}
                        onClick={() => void enviarTexto('¿Qué requisiciones están pendientes?')}
                      >
                        Ver otras pendientes
                      </button>
                    ) : (
                      <button
                        type="button"
                        className={styles.siguientePaso}
                        onClick={() =>
                          void enviarTexto(
                            mensaje.siguientesPasos === 'recibida' ? 'Quiero registrar lo que llegó' : 'Quiero pedir materiales',
                          )
                        }
                      >
                        {mensaje.siguientesPasos === 'creada'
                          ? 'Pedir otros materiales'
                          : mensaje.siguientesPasos === 'recibida'
                            ? 'Registrar otra recepción'
                            : 'Armar otro pedido'}
                      </button>
                    )}
                    {mensaje.siguientesPasos === 'creada' && (
                      <Link to="/obra/requisiciones" className={styles.siguientePaso}>
                        Ver mis requisiciones
                      </Link>
                    )}
                    <Link to="/" className={styles.siguientePaso}>
                      Ir al inicio
                    </Link>
                  </div>
                )}
                {mensaje.borrador && mensaje.estado === 'pendiente' && (
                  <ConfirmacionAccion
                    titulo={mensaje.borrador.titulo}
                    onConfirmar={() => confirmarBorrador(mensaje)}
                    onCancelar={() => cancelarBorrador(mensaje)}
                  >
                    <p className={styles.dato}>Obra: {mensaje.borrador.resumen.obra}</p>
                    {mensaje.borrador.resumen.etapa && (
                      <p className={styles.dato}>Etapa: {mensaje.borrador.resumen.etapa}</p>
                    )}
                    <ul className={styles.items}>
                      {mensaje.borrador.resumen.items.map((item) => (
                        <li key={`${item.descripcion}-${item.cantidad}`}>
                          {formatoCantidad(item.cantidad)} {item.unidadMedida.toLowerCase()} ·{' '}
                          {item.descripcion}
                        </li>
                      ))}
                    </ul>
                  </ConfirmacionAccion>
                )}
                {mensaje.recepcion && mensaje.estado === 'pendiente' && (
                  <ConfirmacionAccion
                    titulo={mensaje.recepcion.titulo}
                    textoConfirmar="Registrar recepción"
                    onConfirmar={() => confirmarRecepcion(mensaje)}
                    onCancelar={() => cancelarRecepcion(mensaje)}
                  >
                    <p className={styles.dato}>Remito: {mensaje.recepcion.resumen.numeroRemito}</p>
                    {mensaje.recepcion.resumen.obra && (
                      <p className={styles.dato}>Obra: {mensaje.recepcion.resumen.obra}</p>
                    )}
                    {mensaje.recepcion.resumen.etapa && (
                      <p className={styles.dato}>Etapa: {mensaje.recepcion.resumen.etapa}</p>
                    )}
                    <ul className={styles.items}>
                      {mensaje.recepcion.resumen.items.map((item, i) => (
                        <li key={`${i}-${item.descripcion}`}>
                          {formatoCantidad(item.cantidad)} {item.unidadMedida.toLowerCase()} · {item.descripcion}
                        </li>
                      ))}
                    </ul>
                    <p className={styles.dato}>Se registra como recibido completo y conforme.</p>
                  </ConfirmacionAccion>
                )}
                {mensaje.aprobacion && mensaje.estado === 'pendiente' && (
                  // Aprobar dispara el proceso de licitación y rechazar cierra la requisición: las dos se muestran
                  // reforzadas y con el nombre del botón claro.
                  <ConfirmacionAccion
                    titulo={mensaje.aprobacion.titulo}
                    reforzada
                    textoConfirmar={mensaje.aprobacion.accion === 'rechazarRequisicion' ? 'Rechazar' : 'Aprobar'}
                    onConfirmar={() => confirmarAprobacion(mensaje)}
                    onCancelar={() => cancelarAprobacion(mensaje)}
                  >
                    <p className={styles.dato}>Requisición: {mensaje.aprobacion.resumen.numero}</p>
                    {mensaje.aprobacion.resumen.obra && (
                      <p className={styles.dato}>Obra: {mensaje.aprobacion.resumen.obra}</p>
                    )}
                    {mensaje.aprobacion.resumen.etapa && (
                      <p className={styles.dato}>Etapa: {mensaje.aprobacion.resumen.etapa}</p>
                    )}
                    {mensaje.aprobacion.accion === 'rechazarRequisicion' && mensaje.aprobacion.motivo && (
                      <p className={styles.dato}>Motivo: {mensaje.aprobacion.motivo}</p>
                    )}
                    <ul className={styles.items}>
                      {mensaje.aprobacion.resumen.items.map((item, i) => (
                        <li key={`${i}-${item.descripcion}`}>
                          {formatoCantidad(item.cantidad)} {item.unidadMedida.toLowerCase()} · {item.descripcion}
                        </li>
                      ))}
                    </ul>
                  </ConfirmacionAccion>
                )}
              </div>
            </div>
          ),
        )}

        {pensando && (
          <div className={styles.delAsistente} role="status">
            <span className={styles.marca} aria-hidden="true">
              <IconoDestello />
            </span>
            <span className={styles.puntos} aria-hidden="true">
              <span />
              <span />
              <span />
            </span>
            <span className={styles.soloLectores}>El asistente está pensando…</span>
          </div>
        )}

        {error && (
          <p role="alert" className={styles.error}>
            {error}
          </p>
        )}
        <div ref={finDeLista} />
      </div>

      <div className={styles.pie}>
        <form className={styles.compositor} onSubmit={enviar}>
          {/* La etiqueta se lee con lector de pantalla, pero no ocupa lugar: el placeholder ya dice qué escribir. */}
          <label htmlFor="mensaje-asistente" className={styles.soloLectores}>
            Escribí tu mensaje
          </label>
          <textarea
            id="mensaje-asistente"
            ref={caja}
            className={styles.caja}
            rows={1}
            maxLength={MAX_CARACTERES_MENSAJE}
            placeholder="Escribí lo que necesitás…"
            value={texto}
            onChange={(e) => setTexto(e.target.value)}
            onKeyDown={alApretarTecla}
            disabled={pensando}
          />
          <button type="submit" className={styles.enviar} aria-label="Enviar" disabled={vacio || pensando}>
            <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
              <path d="M12 19V5" />
              <path d="M5 12l7-7 7 7" />
            </svg>
          </button>
        </form>
        <p className={styles.nota}>
          {texto.length >= CARACTERES_PARA_MOSTRAR_CONTADOR && (
            <span aria-hidden="true">
              {texto.length}/{MAX_CARACTERES_MENSAJE} ·{' '}
            </span>
          )}
          {portal === 'compras' ? (
            <Link to="/compras/requisiciones">Ir a aprobar requisiciones</Link>
          ) : (
            <Link to="/obra/requisicion/nueva">Cargar una requisición a mano</Link>
          )}
        </p>
      </div>
    </section>
  )
}
