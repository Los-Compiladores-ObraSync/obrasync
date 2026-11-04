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
}
