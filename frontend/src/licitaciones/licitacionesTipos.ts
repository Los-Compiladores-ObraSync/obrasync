export interface PliegoItemDto {
  articuloId: string
  descripcion: string
  cantidad: number
  unidadMedida: string
}

export interface LicitacionResponseDto {
  licitacionId: string
  requisicionId: string
  numeroRequisicion?: string
  obraId: string
  estado: string
  fechaCreacion: string
  fechaLimiteCotizacion: string
  items: PliegoItemDto[]
  /** Solo para el Proveedor: true si ya cotizó este pliego. Para Comprador y Admin viene en null. */
  yaCotizada?: boolean | null
}

/** Cuerpo de POST /api/licitaciones: publica el pliego de una requisición aprobada. */
export interface CrearLicitacionRequestDto {
  requisicionId: string
  /** Fecha y hora límite en UTC (ISO 8601). Tiene que ser posterior al momento de publicar. */
  fechaLimiteCotizacion: string
}

export interface CrearCotizacionItemDto {
  articuloId: string
  precioUnitario: number
}

export interface CrearCotizacionRequestDto {
  licitacionId: string
  proveedorId: string
  flete?: number
  plazoEntregaDias?: number
  items: CrearCotizacionItemDto[]
}

export interface CotizacionResponseDto {
  cotizacionId: string
  licitacionId: string
  proveedorId: string
  flete?: number
  plazoEntregaDias?: number
  fechaCreacion: string
  items: CrearCotizacionItemDto[]
  puntajeTotal: number
  esGanadora: boolean
}

export interface AdjudicarLicitacionRequestDto {
  cotizacionId: string
}

export interface OrdenCompraAdjudicadaDto {
  id: string
  licitacionId: string
  cotizacionId: string
  estado: string
  fechaEmision: string
}

