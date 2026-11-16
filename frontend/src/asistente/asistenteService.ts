import { apiFetch, TIEMPO_LIMITE_ASISTENTE_MS } from '../api/client'
import {
  MAX_MENSAJES_HISTORIAL,
  type ChatMensajeDto,
  type ChatRequestDto,
  type ChatResponseDto,
} from './asistenteTipos'

/**
 * Le manda un mensaje al asistente y devuelve su respuesta (y el borrador, si armó uno).
 * Fuente: POST /api/asistente/chat (contracts/openapi/asistente-openapi.json).
 * Se reenvían solo los últimos mensajes: el servidor no guarda conversaciones.
 */
export function enviarMensajeAlAsistente(
  mensaje: string,
  historial: ChatMensajeDto[] = [],
): Promise<ChatResponseDto> {
  const dto: ChatRequestDto = {
    mensaje,
    historial: historial.slice(-MAX_MENSAJES_HISTORIAL),
  }
  return apiFetch<ChatResponseDto>('/api/asistente/chat', {
    method: 'POST',
    body: JSON.stringify(dto),
    // El asistente tarda más que un pedido normal (el servidor le da hasta 45 s).
    tiempoLimiteMs: TIEMPO_LIMITE_ASISTENTE_MS,
  })
}
