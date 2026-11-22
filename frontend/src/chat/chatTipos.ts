/**
 * Tipos sincronizados con el contrato del chat por orden de compra (#218):
 * contracts/openapi/licitaciones-cotizaciones-openapi.json y contracts/realtime/chat-hub.md
 */

/** Un mensaje del chat (MensajeChatOrdenDto). Los mensajes automáticos traen esSistema en true y autorId en null. */
export interface MensajeChatOrdenDto {
  id: string
  ordenCompraId: string
  autorId?: string | null
  autorNombre: string
  /** Comprador, Admin, Proveedor o Sistema. */
  autorRol: string
  esSistema: boolean
  texto: string
  fechaCreacion: string
}

/** Cuerpo de POST /api/ordenes-compra/{id}/chat/mensajes (EnviarMensajeChatRequestDto). */
export interface EnviarMensajeChatRequestDto {
  texto: string
}

/** Respuesta de marcar-leido y evento LecturaActualizada del hub (LecturaChatResponseDto). */
export interface LecturaChatResponseDto {
  ordenCompraId: string
  usuarioId: string
  ultimaLecturaEn: string
}

/** Cuántos mensajes sin leer tiene una orden (NoLeidosOrdenDto). */
export interface NoLeidosOrdenDto {
  ordenCompraId: string
  mensajesNoLeidos: number
}

/** Respuesta de GET /api/ordenes-compra/chat/no-leidos (ResumenNoLeidosChatDto). */
export interface ResumenNoLeidosChatDto {
  totalNoLeidos: number
  ordenes: NoLeidosOrdenDto[]
}

/** Respuesta de problema/resolver y evento ProblemaEstadoActualizado del hub (ProblemaOrdenEstadoDto). */
export interface ProblemaOrdenEstadoDto {
  ordenCompraId: string
  tieneProblemaAbierto: boolean
  motivoProblemaAbierto?: string | null
  fechaProblemaAbierto?: string | null
  problemaResueltoEn?: string | null
  problemaResueltoPor?: string | null
}

/** Estado de la entrega con problema de una orden, como lo muestra el chat. */
export interface EstadoProblema {
  abierto: boolean
  motivo: string | null
}

/** Cómo está la conexión en vivo: en vivo, conectando, o sin conexión (se consulta cada 10 segundos). */
export type EstadoEnVivo = 'conectando' | 'en-vivo' | 'sin-conexion'
