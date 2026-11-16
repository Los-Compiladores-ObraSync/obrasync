/**
 * Tipos sincronizados con contracts/openapi/asistente-openapi.json (borrador v1, #137, #138 y #141).
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

export interface ResumenItemAprobacionDto {
  descripcion: string
  cantidad: number
  unidadMedida: string
}

export interface ResumenAprobacionDto {
  numero: string
  obra?: string | null
  etapa?: string | null
  items: ResumenItemAprobacionDto[]
}

/** Borrador para aprobar UNA requisición Pendiente (#141). Aprueba recién cuando el Comprador toca Confirmar:
 *  el frontend llama a POST /api/requisiciones/{requisicionId}/aprobar con su JWT. */
export interface BorradorAprobacionDto {
  accion: 'aprobarRequisicion'
  titulo: string
  requisicionId: string
  resumen: ResumenAprobacionDto
}

export interface ChatResponseDto {
  respuesta: string
  borrador?: BorradorRequisicionDto | null
  aprobacion?: BorradorAprobacionDto | null
  tokensUsados?: number | null
}
