/**
 * Tipos sincronizados con el contrato de Recepción y Control Presupuestario:
 * contracts/openapi/recepciones-openapi.json
 * y el evento contracts/events/OrdenCompraBloqueadaEvent.json
 */

/** Partida presupuestaria devuelta por GET /api/presupuestos/partidas (PartidaPresupuestariaDto) */
export interface PartidaPresupuestaria {
  id: string
  obraId: string
  etapa: string | null
  codigoPartida: string
  descripcion: string
  montoAsignado: number
  montoComprometido: number
  montoEjecutado: number
  montoDisponible: number
  moneda: string
  createdAt: string
}

/** Filtros opcionales para consultar partidas presupuestarias */
export interface FiltrosPartidas {
  obraId?: string
  etapa?: string
}

/** Cuerpo opcional de POST /api/recepciones/ordenes/{id}/liberar (LiberarOrdenCompraRequest) */
export interface LiberarOrdenCompraRequest {
  motivo?: string | null
}

/** Respuesta de POST /api/recepciones/ordenes/{id}/liberar (OrdenCompraLiberadaDto) */
export interface OrdenCompraLiberada {
  ordenCompraId: string
  obraId: string
  /** 1 = Activa, 2 = Bloqueada */
  estado: number
  liberadaPorUsuarioId: string
  liberadaEn: string
  motivoLiberacion: string | null
  montoTotal: number
  partidaPresupuestariaId: string | null
  montoComprometidoPartida: number | null
}

/** Orden de compra bloqueada devuelta por GET /api/recepciones/ordenes-bloqueadas. */
export interface OrdenCompraBloqueada {
  ordenCompraId: string
  obraId: string
  partidaId: string | null
  etapa: string | null
  montoOrdenCompra: number
  montoAsignadoPartida: number
  montoComprometidoActual: number
  montoProyectado: number
  exceso: number
  motivo: string
  fechaBloqueo: string
}
