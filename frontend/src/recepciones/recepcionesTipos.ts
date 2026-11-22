/**
 * Tipos sincronizados con el contrato de Recepción y Presupuesto:
 * contracts/openapi/recepciones-openapi.json
 */

/** Un material de una orden de compra pendiente (ItemOrdenPendienteDto). */
export interface ItemOrdenPendiente {
  articuloId: string
  descripcionArticulo: string
  cantidadEsperada: number
  cantidadRecibida: number
  /** OpenAPI la expone como propiedad opcional; el frontend puede derivarla del total y acumulado. */
  cantidadPendiente?: number
  unidadMedida: string
}

/** Orden de compra que todavía no se recibió por completo (OrdenPendienteDto). */
export interface OrdenPendiente {
  ordenCompraId: string
  obraId: string
  etapa?: string | null
  sinPartidaPresupuestaria?: boolean
  /** Cuándo despachó el Proveedor (date-time UTC). null si todavía no despachó. */
  fechaDespacho?: string | null
  /** Fecha estimada de llegada que informó el Proveedor al despachar (#216, #217). */
  fechaEstimadaLlegada?: string | null
  /** Observación corta del Proveedor al despachar (máx. 200 caracteres). */
  observacionDespacho?: string | null
  /** true si lo último que se registró sobre la orden fue una entrega con problema, sin una recepción válida después (#217). */
  tieneProblemaAbierto?: boolean
  items: ItemOrdenPendiente[]
}

/** Un material dentro de la recepción (CrearDetalleRecepcionRequest). */
export interface CrearDetalleRecepcionRequest {
  articuloId: string
  descripcionArticulo: string
  cantidadEsperada: number
  cantidadRecibida: number
  unidadMedida: string
  conforme: boolean
  observacion?: string | null
}

/** Cuerpo de POST /api/recepciones (CrearRecepcionRequest). El usuario receptor lo pone el backend. */
export interface CrearRecepcionRequest {
  obraId: string
  ordenCompraId: string
  /** 1 a 50 caracteres. */
  numeroRemito: string
  /** date-time ISO 8601. */
  fechaRecepcion: string
  /** Hasta 500 caracteres. */
  observaciones?: string | null
  detalles: CrearDetalleRecepcionRequest[]
}

/** Lo que el modelo leyó de la foto de un remito (LecturaRemitoDto). Cada campo es null si no se pudo leer. */
export interface LecturaRemito {
  numeroRemito?: string | null
  /** yyyy-MM-dd */
  fecha?: string | null
}

/** Lo que devuelve POST /api/recepciones (RecepcionDto, solo lo que usa la pantalla). */
export interface RecepcionRegistrada {
  id?: string
  obraId?: string
  ordenCompraId?: string
  numeroRemito?: string
  fechaRecepcion?: string | null
  recepcionadoPorUsuarioId?: string
  /** 1 = Pendiente, 2 = Parcial, 3 = Completa, 4 = Rechazada. */
  estado?: number
  detalles?: DetalleRecepcion[]
  observaciones?: string | null
  /** Solo cuando estado = 4 (Rechazada): el motivo que eligió el Capataz (#217). */
  motivoProblema?: MotivoProblema | null
  /** Fotos de evidencia de una entrega con problema (solo los datos; la imagen se baja aparte). */
  fotos?: FotoRecepcion[]
  createdAt?: string
  updatedAt?: string | null
}

export interface DetalleRecepcion {
  id?: string
  recepcionId?: string
  articuloId?: string
  descripcionArticulo?: string
  cantidadEsperada?: number
  cantidadRecibida?: number
  unidadMedida?: string
  conforme?: boolean
  observacion?: string | null
  createdAt?: string
  updatedAt?: string | null
}

export interface RecepcionHistorial extends RecepcionRegistrada {
  detalles?: DetalleRecepcion[]
}

/** Los motivos que acepta el servicio, con el texto exacto del contrato (RecepcionDto.motivoProblema). */
export const MOTIVOS_PROBLEMA = ['No llegó nada', 'Llegó incompleta', 'Llegó dañada', 'No es lo pedido'] as const
export type MotivoProblema = (typeof MOTIVOS_PROBLEMA)[number]

/** Datos de una foto de evidencia (FotoRecepcionDto). La imagen se descarga con la ruta autenticada de `url`. */
export interface FotoRecepcion {
  id: string
  nombreArchivo?: string
  contentType?: 'image/jpeg' | 'image/png' | 'image/webp'
  tamanoBytes?: number
  /** 1 a 3. */
  orden?: number
  /** Ruta relativa autenticada: /api/recepciones/{id}/fotos/{fotoId}. */
  url?: string
}

/**
 * Orden con su historial de recepciones, problemas y fotos (RecepcionesDeOrdenDto).
 * Fuente: GET /api/recepciones/ordenes/{ordenCompraId}.
 */
export interface RecepcionesDeOrden {
  ordenCompraId: string
  obraId: string
  etapa?: string | null
  fechaDespacho?: string | null
  fechaEstimadaLlegada?: string | null
  observacionDespacho?: string | null
  /** true si ya hay una recepción Completa. */
  estaCompleta?: boolean
  tieneProblemaAbierto?: boolean
  items: ItemOrdenPendiente[]
  /** En orden cronológico. */
  recepciones: RecepcionHistorial[]
}

/** Campos de texto de POST /api/recepciones/problemas (las fotos se mandan aparte, como archivos). */
export interface EntregaConProblemaRequest {
  obraId: string
  ordenCompraId: string
  motivo: MotivoProblema
  /** Hasta 500 caracteres. */
  detalle?: string | null
  /** Hasta 50 caracteres. Si se omite, el servicio genera uno interno. */
  numeroRemito?: string | null
}
