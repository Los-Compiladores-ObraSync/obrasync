/**
 * Tipos sincronizados con los contratos OpenAPI de Requisiciones y Catálogo y Usuarios/Obras.
 * Fuentes:
 * - contracts/openapi/requisiciones-catalogo-openapi.json
 * - contracts/openapi/usuarios-obras-openapi.json
 */

export interface InsumoCatalogo {
  articuloId: string
  codigo: string
  descripcion: string
  unidadMedida: string
  moneda: string
  precioReferencia?: number | null
  activo: boolean
}

export interface CrearRequisicionItemDto {
  articuloId: string
  cantidad: number
}

export interface CrearRequisicionRequestDto {
  obraId: string
  etapa?: string | null
  observaciones?: string | null
  items: CrearRequisicionItemDto[]
}

export interface RequisicionItemDto {
  articuloId: string
  descripcion: string
  cantidad: number
  unidadMedida: string
}

export type EstadoRequisicion = 'Pendiente' | 'Aprobada' | 'Rechazada'

export interface RequisicionResponseDto {
  requisicionId: string
  numeroRequisicion: string
  obraId: string
  etapa?: string | null
  solicitanteId: string
  aprobadorId?: string | null
  estado: EstadoRequisicion
  observaciones?: string | null
  fechaCreacion: string
  fechaAprobacion?: string | null
  items: RequisicionItemDto[]
}

export interface EtapaObraResumen {
  id: string
  nombreEtapa: string
  estado: string
}

export interface ObraResumen {
  id: string
  nombre: string
  ubicacion: string
  fechaInicio: string
  capataz?: string | null
  etapas: EtapaObraResumen[]
}
