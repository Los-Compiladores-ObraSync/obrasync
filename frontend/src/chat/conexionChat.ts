import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { obtenerToken } from '../api/client'
import { API_BASE_URL } from '../api/config'
import { REINTENTO_CONEXION_MS } from './chatConfig'
import type {
  EstadoEnVivo,
  LecturaChatResponseDto,
  MensajeChatOrdenDto,
  ProblemaOrdenEstadoDto,
} from './chatTipos'

/** Ruta del hub de tiempo real (contracts/realtime/chat-hub.md). Pasa por el Gateway como el resto de /api. */
export const RUTA_HUB_CHAT = '/api/ordenes-compra/chat-hub'

export interface ManejadoresChat {
  onMensaje: (mensaje: MensajeChatOrdenDto) => void
  onLectura: (lectura: LecturaChatResponseDto) => void
  onProblema: (problema: ProblemaOrdenEstadoDto) => void
  onEstado: (estado: EstadoEnVivo) => void
}

export interface ConexionChat {
  cerrar: () => void
}

/**
 * Abre la conexión en vivo (SignalR) del chat de una orden y se une a su grupo.
 * El hub solo avisa (mensaje nuevo, lectura, problema): escribir siempre es por REST.
 *
 * - El JWT viaja por parámetro de la conexión (access_token), como define el contrato.
 * - Si el hub no se puede abrir, o se corta del todo, avisa "sin-conexion" y vuelve a intentar cada tanto.
 *   Mientras tanto la pantalla consulta por su cuenta (respaldo).
 * - Es el único archivo que importa el paquete de SignalR.
 */
export function abrirConexionChat(ordenCompraId: string, manejadores: ManejadoresChat): ConexionChat {
  const conexion = new HubConnectionBuilder()
    .withUrl(`${API_BASE_URL}${RUTA_HUB_CHAT}`, { accessTokenFactory: () => obtenerToken() ?? '' })
    // Cortes cortos: reintenta enseguida y después más espaciado. Si agota los intentos, onclose lo retoma.
    .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
    .configureLogging(LogLevel.None)
    .build()

  let cerrada = false
  let temporizador: ReturnType<typeof setTimeout> | undefined

  conexion.on('MensajeRecibido', (mensaje: MensajeChatOrdenDto) => manejadores.onMensaje(mensaje))
  conexion.on('LecturaActualizada', (lectura: LecturaChatResponseDto) => manejadores.onLectura(lectura))
  conexion.on('ProblemaEstadoActualizado', (problema: ProblemaOrdenEstadoDto) => manejadores.onProblema(problema))

  async function unirse() {
    await conexion.invoke('UnirseAOrden', ordenCompraId)
    if (!cerrada) manejadores.onEstado('en-vivo')
  }

  function programarReintento() {
    if (cerrada) return
    temporizador = setTimeout(() => void iniciar(), REINTENTO_CONEXION_MS)
  }

  async function iniciar() {
    if (cerrada) return
    manejadores.onEstado('conectando')
    try {
      await conexion.start()
      await unirse()
    } catch {
      if (cerrada) return
      manejadores.onEstado('sin-conexion')
      programarReintento()
    }
  }

  conexion.onreconnecting(() => {
    if (!cerrada) manejadores.onEstado('sin-conexion')
  })
  // Al reconectar el servidor ya no recuerda el grupo: hay que volver a unirse.
  conexion.onreconnected(() => {
    unirse().catch(() => {
      if (!cerrada) manejadores.onEstado('sin-conexion')
    })
  })
  conexion.onclose(() => {
    if (cerrada) return
    manejadores.onEstado('sin-conexion')
    programarReintento()
  })

  void iniciar()

  return {
    cerrar() {
      cerrada = true
      if (temporizador) clearTimeout(temporizador)
      void conexion.stop()
    },
  }
}
