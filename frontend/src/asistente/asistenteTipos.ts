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

/** Borrador para aprobar (#141) o rechazar (#149) UNA requisición Pendiente, o para publicar (#149) la licitación de
 *  una Aprobada. No hace nada hasta que el Comprador toca Confirmar: entonces el frontend llama, con su JWT, a
 *  POST /api/requisiciones/{requisicionId}/aprobaciones, a .../rechazos con { motivo }, o a POST /api/licitaciones. */
export interface BorradorAprobacionDto {
  accion: 'aprobarRequisicion' | 'rechazarRequisicion' | 'publicarLicitacion'
  titulo: string
  requisicionId: string
  resumen: ResumenAprobacionDto
  /** Solo viene al rechazar: el motivo que dijo el Comprador (1 a 500 caracteres). */
  motivo?: string | null
  /** Solo viene al publicar: días para cotizar desde hoy (1 a 30). La fecha límite la calcula el frontend en hora local. */
  diasParaCotizar?: number | null
}

export interface ResumenRecepcionDto {
  obra?: string | null
  etapa?: string | null
  numeroRemito: string
  items: ResumenItemAprobacionDto[]
}

/** Cuerpo de POST /api/recepciones SIN la fecha: la agrega el frontend al confirmar. */
export interface CuerpoRecepcionDto {
  obraId: string
  ordenCompraId: string
  numeroRemito: string
  observaciones?: string | null
  detalles: {
    articuloId: string
    descripcionArticulo: string
    cantidadEsperada: number
    cantidadRecibida: number
    unidadMedida: string
    conforme: boolean
  }[]
}

/** Borrador de una recepción TOTAL (#147). Registra recién cuando el Capataz toca Confirmar:
 *  el frontend llama a POST /api/recepciones con su JWT. */
export interface BorradorRecepcionDto {
  accion: 'registrarRecepcion'
  titulo: string
  resumen: ResumenRecepcionDto
  recepcion: CuerpoRecepcionDto
}

export interface ResumenAdjudicacionDto {
  numeroRequisicion?: string | null
  obra?: string | null
  /** null mientras Licitaciones no devuelva el nombre (#206): se muestra el código. */
  proveedorNombre?: string | null
  /** Primeros 8 caracteres del proveedorId. */
  proveedorCodigo: string
  precioMateriales: number
  flete: number
  /** Precio de los materiales más el flete, tal como lo calcula el backend: es el monto de la Orden de Compra. */
  costoTotal: number
  plazoEntregaDias?: number | null
  puntajeTotal: number
  /** La oferta elegida es la de mayor puntaje. */
  esGanadora: boolean
  /** La oferta elegida NO es la ganadora y hay otra que sí lo es: la tarjeta se lo avisa a la persona. */
  hayOfertaConMayorPuntaje: boolean
  items: ResumenItemAprobacionDto[]
}

/** Borrador de adjudicar (#149) UNA oferta de una licitación Abierta. ES IRREVERSIBLE: al confirmar se emite la Orden de
 *  Compra. No hace nada hasta que el Comprador toca Confirmar: entonces el frontend llama, con su JWT, a
 *  POST /api/licitaciones/{licitacionId}/adjudicaciones con { cotizacionId }. */
export interface BorradorAdjudicacionDto {
  accion: 'adjudicarCotizacion'
  titulo: string
  licitacionId: string
  cotizacionId: string
  resumen: ResumenAdjudicacionDto
}

export interface ChatResponseDto {
  respuesta: string
  borrador?: BorradorRequisicionDto | null
  aprobacion?: BorradorAprobacionDto | null
  recepcion?: BorradorRecepcionDto | null
  adjudicacion?: BorradorAdjudicacionDto | null
  tokensUsados?: number | null
}
