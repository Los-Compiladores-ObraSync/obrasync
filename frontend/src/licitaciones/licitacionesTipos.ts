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
  /** Calculados por el servicio (contrato: CotizacionResponseDto). Si no vienen, la pantalla los calcula. */
  subtotalItems?: number
  costoTotal?: number
  esMasBarata?: boolean
  /** PROPUESTA de contrato (pendiente de Licitaciones): si no vienen, la pantalla no muestra esos datos. */
  proveedorNombre?: string | null
  /** Promedio de 1 a 5 (con decimales) de las calificaciones recibidas, o null si no tiene. */
  calificacionProveedor?: number | null
  /** Puntos (0 a 100) que aportó cada criterio al puntajeTotal; la suma da puntajeTotal. */
  desglosePuntaje?: CriteriosScoringDto | null
  /** Peso de cada criterio (0 a 1) con el que se calculó el puntaje. */
  ponderacionesScoring?: CriteriosScoringDto | null
  /** Qué tan buena es la oferta en cada criterio (0 a 100, comparada con las demás) y el peso aplicado. */
  desgloseScoring?: DesgloseScoringDto | null
}

/** Contrato: DesgloseScoringDto. Los puntajes van de 0 a 100 y no dependen del peso; los pesos van de 0 a 1. */
export interface DesgloseScoringDto {
  puntajePrecio: number
  puntajePlazo: number
  puntajeFlete: number
  puntajeCalificacion: number
  pesoPrecio: number
  pesoPlazo: number
  pesoFlete: number
  pesoCalificacion: number
}

/** Los cuatro criterios del scoring multivariable (contrato: CriteriosScoringDto). */
export interface CriteriosScoringDto {
  precio: number
  flete: number
  plazo: number
  calificacion: number
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

/** Una orden de compra del listado de Compras (#205). Fuente: GET /api/ordenes-compra. */
export interface OrdenCompraListadoDto {
  id: string
  licitacionId: string
  cotizacionId: string
  obraId: string
  proveedorId: string
  /** "Emitida" o "Despachada". */
  estado: string
  fechaEmision: string
  fueDespachada: boolean
  /** Cuándo se marcó el despacho; null mientras no se despachó. */
  fechaDespacho?: string | null
}

/** Calificación vigente de un proveedor (contrato: CalificacionProveedorResponseDto, #88). */
export interface CalificacionProveedorResponseDto {
  proveedorId: string
  /** Promedio de 1 a 5, con hasta 2 decimales, de las calificaciones de distintos usuarios. */
  calificacion: number
  /** Cuántas calificaciones (de distintos usuarios) forman el promedio. */
  cantidadCalificaciones: number
  fechaActualizacion: string
}

/**
 * Una cotización propia del Proveedor (contrato: MiCotizacionResponseDto, GET /api/cotizaciones/mias).
 * estadoResultado: "Enviada" (licitación abierta, sin definir), "Gano", "NoGano" o "LicitacionCerrada".
 */
export interface MiCotizacionDto {
  cotizacionId: string
  licitacionId: string
  proveedorId: string
  numeroRequisicion: string
  obraId: string
  fechaLimiteCotizacion: string
  estadoLicitacion: string
  subtotalItems: number
  flete: number
  costoTotal: number
  coberturaParcial: boolean
  plazoEntregaDias: number
  fechaCreacion: string
  items: CrearCotizacionItemDto[]
  esGanadora: boolean
  estadoResultado: string
}
