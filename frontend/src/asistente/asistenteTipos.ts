/**
 * Tipos sincronizados con contracts/openapi/asistente-openapi.json (borrador v1, #137 y #138).
 * Regla de oro: el asistente NUNCA escribe datos. Solo responde y, si corresponde, arma un borrador;
 * la escritura la hace el frontend con el endpoint existente cuando el usuario toca Confirmar.
 */
import type { CrearRequisicionRequestDto } from '../requisiciones/requisicionesTipos'

/** Máximo de caracteres de un mensaje (el mismo límite que valida el servidor). */
export const MAX_CARACTERES_MENSAJE = 500
/** Máximo de mensajes de historial que se reenvían en cada pedido. */
export const MAX_MENSAJES_HISTORIAL = 10

export interface ChatMensajeDto {
  rol: 'usuario' | 'asistente'
  texto: string
}

export interface ChatRequestDto {
  mensaje: string
  historial?: ChatMensajeDto[]
}

export interface ResumenItemDto {
  descripcion: string
  cantidad: number
  unidadMedida: string
}

export interface ResumenBorradorDto {
  obra: string
  etapa?: string | null
  items: ResumenItemDto[]
}

export interface BorradorRequisicionDto {
  accion: 'crearRequisicion'
  titulo: string
  resumen: ResumenBorradorDto
  requisicion: CrearRequisicionRequestDto
}

export interface ChatResponseDto {
  respuesta: string
  borrador?: BorradorRequisicionDto | null
  tokensUsados?: number | null
}
