import type { PliegoItemDto } from './licitacionesTipos'

/**
 * Lo que ve el proveedor al abrir su Magic Link: solo lo necesario para cotizar (contrato: PliegoMagicLinkResponseDto).
 * No trae la obra ni la requisición.
 */
export interface PliegoMagicLinkDto {
  licitacionId: string
  numeroRequisicion: string
  /** Hasta cuándo se puede cotizar (UTC). */
  fechaLimiteCotizacion: string
  /** Hasta cuándo se puede usar el enlace (UTC). */
  fechaDeVencimientoEnlace: string
  /** El nombre que escribió Compras al generar el enlace; puede faltar. */
  proveedorNombre?: string | null
  items: PliegoItemDto[]
}

/** Un ítem de la cotización hecha con un Magic Link. Es el mismo formato de siempre (contrato: items de POST .../cotizar). */
export interface CotizarConMagicLinkItemDto {
  articuloId: string
  precioUnitario: number
  /** Otra presentación (ej.: "bolsa 25 kg", #190). Hasta 100 caracteres. Opcional. */
  presentacionOfrecida?: string | null
  /** Cantidad ofrecida en esa presentación (mínimo 0,01). Opcional. */
  cantidadOfrecida?: number | null
}

/** Cuerpo de POST /api/licitaciones/magic-links/{token}/cotizar. No lleva proveedor: lo identifica el enlace. */
export interface CotizarConMagicLinkRequestDto {
  flete: number
  plazoEntregaDias: number
  items: CotizarConMagicLinkItemDto[]
}
