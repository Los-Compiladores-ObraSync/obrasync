/**
 * Tipos de la carga de insumos (#215). Fuente: CrearInsumoRequestDto en
 * contracts/openapi/requisiciones-catalogo-openapi.json (POST /api/catalogo/insumos).
 */
export interface CrearInsumoRequest {
  codigo: string
  descripcion: string
  unidadMedida: string
  /** Divisa única del catálogo; si no se manda, el servidor usa ARS. */
  moneda?: string
  precioReferencia?: number | null
  /** Si no se manda, el insumo nace activo. */
  activo?: boolean
}
