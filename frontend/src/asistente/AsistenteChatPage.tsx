import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { Link } from 'react-router'
import { ApiError } from '../api/ApiError'
import {
  aprobarRequisicion,
  crearRequisicion,
  generarIdempotencyKey,
  rechazarRequisicion,
} from '../requisiciones/requisicionesService'
import { adjudicarLicitacion, publicarLicitacion } from '../licitaciones/licitacionesService'
import { formatoPrecio } from '../licitaciones/cotizacionesCalculos'
import { fechaLocalEnDias, finDelDiaEnUtc } from '../licitaciones/plazoCotizacion'
import { fechaAIso } from '../obras/validacionObra'
import { hoyComoTexto } from '../obras/fechaEnPalabras'
import { registrarRecepcion } from '../recepciones/recepcionesService'
import { fechaLarga } from '../requisiciones/formatoFecha'
import { enviarMensajeAlAsistente } from './asistenteService'
import {
  MAX_CARACTERES_MENSAJE,
  type BorradorAdjudicacionDto,
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
  siguientesPasos?: 'creada' | 'descartada' | 'aprobada' | 'rechazada' | 'publicada' | 'adjudicada' | 'recibida'
  borrador?: BorradorRequisicionDto
  /** Borrador de aprobar, rechazar o publicar la licitación de una requisición (portal de Compras, #141 y #149). */
  aprobacion?: BorradorAprobacionDto
  /** Solo al publicar: "YYYY-MM-DD" local hasta el que cotizan los proveedores. Se fija UNA vez, cuando llega el borrador,
   *  para que la fecha que la persona ve en la tarjeta sea la misma que se envía aunque confirme más tarde. */
  fechaLimiteLocal?: string
  /** Borrador de adjudicar una oferta (portal de Compras, #149). Irreversible: emite la Orden de Compra. */
  adjudicacion?: BorradorAdjudicacionDto
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

/** Lo que dice el botón "Comparar ofertas": es el mismo pedido desde la pantalla de inicio y desde el botón fijo del chat.
 *  Pide la última licitación abierta para que el asistente responda de una: si preguntara "¿cuál?", cada vuelta extra gasta
 *  del cupo de IA, que es compartido por todo el grupo. Para otra licitación, la persona la pide escribiendo. */
const MENSAJE_COMPARAR_OFERTAS = 'Comparame las ofertas de la última licitación abierta'

/** Lo que el asistente sabe hacer en el portal de Compras (#141, #148 y #149): consultar, comparar ofertas, aprobar, rechazar, publicar y adjudicar con confirmación. */
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
  {
    titulo: 'Publicar una licitación',
    descripcion: 'Armo la publicación de una requisición aprobada, para que los proveedores coticen. Vos la confirmás.',
    mensaje: 'Quiero publicar una licitación',
    disponible: true,
  },
  {
    titulo: 'Comparar ofertas',
    descripcion: 'Te cuento qué ofertas llegaron a una licitación y cuál conviene. Solo consulto, no cambio nada.',
    mensaje: MENSAJE_COMPARAR_OFERTAS,
    disponible: true, // #148
  },
  {
    titulo: 'Adjudicar una oferta',
    descripcion: 'Te armo la adjudicación de la oferta que elijas. Es irreversible: se emite la Orden de Compra, y solo la hacés vos al confirmar.',
    mensaje: 'Quiero adjudicar una oferta',
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

/** La fecha límite (hora local de esta persona) de un borrador de publicación; undefined para cualquier otro borrador. */
function fechaLimiteDelBorrador(borrador: BorradorAprobacionDto | null | undefined): string | undefined {
  if (borrador?.accion !== 'publicarLicitacion' || !borrador.diasParaCotizar) {
    return undefined
  }
  return fechaLocalEnDias(borrador.diasParaCotizar)
}

/** El proveedor para la tarjeta: el nombre, o su código si Licitaciones todavía no lo devuelve (#206). */
function proveedorEnTarjeta(borrador: BorradorAdjudicacionDto): string {
  return borrador.resumen.proveedorNombre?.trim() || `código ${borrador.resumen.proveedorCodigo} (nombre no disponible)`
}

/** La oferta como se nombra en una frase: "la oferta de Ferretería Sur" o "la oferta del proveedor con código a1a1a1a1". */
function ofertaEnFrase(borrador: BorradorAdjudicacionDto): string {
  const nombre = borrador.resumen.proveedorNombre?.trim()
  return nombre ? `la oferta de ${nombre}` : `la oferta del proveedor con código ${borrador.resumen.proveedorCodigo}`
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
        fechaLimiteLocal: fechaLimiteDelBorrador(respuesta.aprobacion),
        recepcion: respuesta.recepcion ?? undefined,
        adjudicacion: respuesta.adjudicacion ?? undefined,
        claveIdempotencia: respuesta.borrador ? generarIdempotencyKey() : undefined,
        estado:
          respuesta.borrador || respuesta.aprobacion || respuesta.recepcion || respuesta.adjudicacion
            ? 'pendiente'
            : undefined,
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
    if (borrador.accion === 'publicarLicitacion') {
      if (!mensaje.fechaLimiteLocal) {
        return
      }
      const licitacion = await publicarLicitacion({
        requisicionId: borrador.requisicionId,
        fechaLimiteCotizacion: finDelDiaEnUtc(mensaje.fechaLimiteLocal),
      })
      actualizarMensaje(mensaje.id, { estado: 'confirmada' })
      const hasta = fechaLarga(licitacion.fechaLimiteCotizacion)
      agregarMensaje({
        rol: 'asistente',
        texto:
          `Listo, publiqué la licitación de la requisición ${borrador.resumen.numero}.` +
          (hasta ? ` Los proveedores pueden cotizar hasta el ${hasta}.` : ''),
        local: true,
        siguientesPasos: 'publicada',
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

  async function confirmarAdjudicacion(mensaje: MensajeChat) {
    const borrador = mensaje.adjudicacion
    if (!borrador) {
      return
    }
    // Si falla (409: ya está adjudicada; 404: la oferta o la licitación ya no existe; 400: la oferta no es de esa licitación),
    // el error lo muestra la tarjeta con la explicación del servidor y NO se dice que se adjudicó.
    await adjudicarLicitacion(borrador.licitacionId, borrador.cotizacionId)
    actualizarMensaje(mensaje.id, { estado: 'confirmada' })
    agregarMensaje({
      rol: 'asistente',
      texto: `Listo, adjudiqué ${ofertaEnFrase(borrador)}. Se emitió la Orden de Compra por ${formatoPrecio(borrador.resumen.costoTotal)}.`,
      local: true,
      siguientesPasos: 'adjudicada',
    })
  }

  function cancelarAdjudicacion(mensaje: MensajeChat) {
    actualizarMensaje(mensaje.id, { estado: 'cancelada' })
    agregarMensaje({
      rol: 'asistente',
      texto: 'Listo, no adjudiqué nada. La licitación sigue abierta.',
      local: true,
      siguientesPasos: 'descartada',
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
          : mensaje.aprobacion?.accion === 'publicarLicitacion'
            ? 'Listo, no publiqué nada. La requisición sigue aprobada, sin licitación.'
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
        <Link to="/" className={styles.volver}>
          ← Volver al inicio
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
                {mensaje.adjudicacion && mensaje.estado === 'pendiente' && (
                  // Adjudicar emite la Orden de Compra y no se puede deshacer: tarjeta reforzada, con el total bien visible.
                  <ConfirmacionAccion
                    titulo={mensaje.adjudicacion.titulo}
                    reforzada
                    textoConfirmar="Adjudicar"
                    onConfirmar={() => confirmarAdjudicacion(mensaje)}
                    onCancelar={() => cancelarAdjudicacion(mensaje)}
                  >
                    <p className={styles.dato}>Proveedor: {proveedorEnTarjeta(mensaje.adjudicacion)}</p>
                    {mensaje.adjudicacion.resumen.numeroRequisicion && (
                      <p className={styles.dato}>Requisición: {mensaje.adjudicacion.resumen.numeroRequisicion}</p>
                    )}
                    {mensaje.adjudicacion.resumen.obra && (
                      <p className={styles.dato}>Obra: {mensaje.adjudicacion.resumen.obra}</p>
                    )}
                    <p className={styles.dato}>
                      Total de la Orden de Compra: {formatoPrecio(mensaje.adjudicacion.resumen.costoTotal)}
                    </p>
                    <p className={styles.dato}>
                      Materiales {formatoPrecio(mensaje.adjudicacion.resumen.precioMateriales)} + flete{' '}
                      {formatoPrecio(mensaje.adjudicacion.resumen.flete)}
                    </p>
                    {mensaje.adjudicacion.resumen.plazoEntregaDias != null && (
                      <p className={styles.dato}>Plazo de entrega: {mensaje.adjudicacion.resumen.plazoEntregaDias} días</p>
                    )}
                    <p className={styles.dato}>
                      Puntaje: {formatoCantidad(mensaje.adjudicacion.resumen.puntajeTotal)}
                      {mensaje.adjudicacion.resumen.esGanadora ? ' (la de mayor puntaje)' : ''}
                    </p>
                    {mensaje.adjudicacion.resumen.hayOfertaConMayorPuntaje && (
                      <p role="note" className={styles.dato}>
                        Atención: esta oferta no es la de mayor puntaje.
                      </p>
                    )}
                    <ul className={styles.items}>
                      {mensaje.adjudicacion.resumen.items.map((item, i) => (
                        <li key={`${i}-${item.descripcion}`}>
                          {formatoCantidad(item.cantidad)} {item.unidadMedida.toLowerCase()} · {item.descripcion}
                        </li>
                      ))}
                    </ul>
                  </ConfirmacionAccion>
                )}
                {mensaje.aprobacion &&
                  mensaje.estado === 'pendiente' &&
                  // Publicar necesita la fecha límite fijada al llegar el borrador; sin ella no se ofrece la tarjeta (no escribe nada).
                  (mensaje.aprobacion.accion !== 'publicarLicitacion' || mensaje.fechaLimiteLocal !== undefined) && (
                  // Aprobar dispara el proceso de licitación y rechazar cierra la requisición: se muestran reforzadas.
                  // Publicar no se marca como irreversible (no sabemos si hay forma de cancelar la licitación), pero igual pide confirmar.
                  <ConfirmacionAccion
                    titulo={mensaje.aprobacion.titulo}
                    reforzada={mensaje.aprobacion.accion !== 'publicarLicitacion'}
                    textoConfirmar={
                      mensaje.aprobacion.accion === 'rechazarRequisicion'
                        ? 'Rechazar'
                        : mensaje.aprobacion.accion === 'publicarLicitacion'
                          ? 'Publicar'
                          : 'Aprobar'
                    }
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
                    {mensaje.aprobacion.accion === 'publicarLicitacion' && mensaje.fechaLimiteLocal && (
                      <p className={styles.dato}>
                        Los proveedores cotizan hasta el {fechaLarga(finDelDiaEnUtc(mensaje.fechaLimiteLocal))}
                      </p>
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
        {/* Una vez empezada la charla ya no se ve la pantalla de inicio: este botón deja a mano la comparación de ofertas.
            Solo manda un mensaje al asistente (que consulta, no cambia nada), igual que escribirlo. */}
        {portal === 'compras' && !conversacionNueva && (
          <div className={styles.accionesRapidas} role="group" aria-label="Acciones rápidas">
            <button
              type="button"
              className={styles.siguientePaso}
              disabled={pensando}
              onClick={() => void enviarTexto(MENSAJE_COMPARAR_OFERTAS)}
            >
              Comparar ofertas
            </button>
          </div>
        )}
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
