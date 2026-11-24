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
  /** Fecha UTC real de adjudicación; es null cuando no fue adjudicada o es anterior a su persistencia. */
  fechaAdjudicacion?: string | null
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
  /** Precio por unidad de lo que se ofrece (si hay otra presentación, es por unidad de esa presentación). */
  precioUnitario: number
  /** Otra presentación ofrecida (ej.: "bolsa 25 kg", #190). Hasta 100 caracteres. Opcional. */
  presentacionOfrecida?: string | null
  /** Cantidad ofrecida en esa presentación (mínimo 0,01). Si se omite, el servicio toma la cantidad del pliego. */
  cantidadOfrecida?: number | null
  /** Solo viene en las respuestas: (cantidadOfrecida ?? cantidad del pliego) × precioUnitario. No se envía. */
  subtotalItem?: number
  /** Solo viene en las respuestas: true si lo ofrecido no cubre lo pedido. Lo calcula el servicio; no se envía. */
  coberturaParcial?: boolean
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
  /** true si algún ítem no cubre todo lo pedido (#190). Lo calcula el servicio. */
  coberturaParcial?: boolean
  /** PROPUESTA de contrato (pendiente de Licitaciones): si no vienen, la pantalla no muestra esos datos. */
  proveedorNombre?: string | null
  /** Promedio de 1 a 5 (con decimales) de las calificaciones recibidas, o null si no tiene. */
  calificacionProveedor?: number | null
  /** Cantidad de calificaciones que forman la reputación del proveedor (#222). */
  cantidadCalificacionesProveedor?: number
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
  /** Quién despachó (el Proveedor dueño de la orden, #216). */
  despachadaPor?: string | null
  /** Fecha estimada de llegada que informó el Proveedor al despachar (opcional). */
  fechaEstimadaLlegada?: string | null
  /** Observación del despacho (opcional, hasta 200 caracteres). */
  observacion?: string | null
  /** true si lo último que pasó con la entrega fue un problema, sin una recepción válida después (#217, #218). */
  tieneProblemaAbierto?: boolean
  /** El motivo del problema abierto: "No llegó nada", "Llegó incompleta", "Llegó dañada" o "No es lo pedido". */
  motivoProblemaAbierto?: string | null
  /** true si Recepción confirmó que llegó todo el pedido (#222). Habilita calificar la orden. */
  recibidaCompleta?: boolean
  /** Cuándo se confirmó la recepción completa (UTC); null si todavía no. */
  fechaRecepcionCompleta?: string | null
  /** Calificación (1 a 5) que el usuario de Compras ya cargó para esta orden; null si todavía no (#222). */
  miCalificacion?: number | null
  /** Comentario opcional del usuario de Compras para esta orden (#222). */
  miComentarioCalificacion?: string | null
  /** Fecha de actualización de la calificación del usuario de Compras (#222). */
  fechaMiCalificacion?: string | null
  /** Mensajes del chat de la orden que el usuario autenticado todavía no leyó (#218). */
  mensajesNoLeidos?: number
  /** Cuántos envíos tiene la orden: 0 sin despachar, 1 el primer despacho, 2 o más tras re-despachos del remanente (#248). */
  numeroDespacho?: number
  /** true si el Proveedor puede despachar de nuevo lo que falta tras una recepción parcial (#248). */
  puedeRedespachar?: boolean
  /** true si la última recepción fue parcial y quedó un remanente pendiente de envío (#248). */
  tieneRecepcionParcialPendiente?: boolean
  /** Cuándo fue la última recepción parcial (UTC); null si no aplica (#248). */
  fechaUltimaRecepcionParcial?: string | null
  items?: OrdenCompraItemDto[] | null
}

export interface OrdenCompraItemDto {
  articuloId: string
  descripcion: string
  cantidad: number
  unidadMedida: string
  presentacionAdjudicada?: string | null
}

/** Un comentario de calificación recibido por el Proveedor (contrato: ComentarioCalificacionProveedorDto, #222). */
export interface ComentarioCalificacionDto {
  ordenCompraId?: string | null
  /** Estrellas, de 1 a 5. */
  calificacion: number
  comentario: string
  fechaActualizacion: string
}

/** Reputación del Proveedor autenticado (contrato: ReputacionProveedorResponseDto, #222). */
export interface ReputacionProveedorDto {
  proveedorId: string
  calificacion?: number | null
  cantidadCalificaciones: number
  fechaActualizacion?: string | null
  /** Hasta 20, del más reciente al más antiguo. */
  ultimosComentarios: ComentarioCalificacionDto[]
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

/** Calificación del usuario actual sobre una orden recibida completa (#222). */
export interface CalificacionOrdenCompraResponseDto {
  ordenCompraId: string
  proveedorId: string
  usuarioId: string
  calificacion: number
  comentario?: string | null
  fechaActualizacion: string
  promedioProveedor: number
  cantidadCalificacionesProveedor: number
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

/**
 * Respuesta de POST /api/licitaciones/{id}/magic-links (GenerarMagicLinkResponseDto del contrato).
 * El token en claro viaja una sola vez: en la base solo queda su hash.
 */
export interface MagicLinkGeneradoDto {
  token: string
  /** Alias del token (compatibilidad). */
  hash: string
  /** Ruta de la API para cotizar con el enlace; no es una pantalla, no se le pasa al proveedor. */
  url: string
  /** Vencimiento exacto, en UTC. */
  fechaDeVencimiento: string
  /** Duración en texto del backend (en inglés, ej. "1 hour"); para mostrar usamos horasVigencia. */
  expiresIn: string
  horasVigencia: number
}
