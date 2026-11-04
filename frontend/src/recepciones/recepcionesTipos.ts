/**
 * Tipos sincronizados con el contrato de Recepción y Presupuesto:
 * contracts/openapi/recepciones-openapi.json
 */

/** Un material de una orden de compra pendiente (ItemOrdenPendienteDto). */
export interface ItemOrdenPendiente {
  articuloId: string
  descripcionArticulo: string
  cantidadEsperada: number
  /** Lo ya recibido en total (hoy siempre 0: no hay recepciones parciales hasta el Sprint 3). */
  cantidadRecibida: number
  unidadMedida: string
}

/** Orden de compra que todavía no se recibió por completo (OrdenPendienteDto). */
export interface OrdenPendiente {
  ordenCompraId: string
  obraId: string
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

/** Lo que devuelve POST /api/recepciones (RecepcionDto, solo lo que usa la pantalla). */
export interface RecepcionRegistrada {
  id: string
  obraId: string
  ordenCompraId: string
  numeroRemito: string
  fechaRecepcion: string
  /** 1 = Pendiente, 2 = Parcial, 3 = Completa, 4 = Rechazada. */
  estado: number
}
