import { apiFetch } from '../api/client'

/** Una etapa de una plantilla. Es el EtapaPlantillaDto del contrato. */
export interface EtapaPlantilla {
  id: string
  nombreEtapa: string
  /** Posición de la etapa dentro de la plantilla (1, 2, 3…). */
  orden: number
}

/** Una plantilla de obra. Es el PlantillaObraResponseDto del contrato. */
export interface PlantillaObra {
  id: string
  nombre: string
  descripcion: string
  etapas: EtapaPlantilla[]
}

/**
 * Lista las plantillas de obra preconfiguradas (Admin, Comprador y Capataz).
 * Fuente: GET /api/plantillas-obra (contracts/openapi/usuarios-obras-openapi.json)
 */
export function obtenerPlantillas(): Promise<PlantillaObra[]> {
  return apiFetch<PlantillaObra[]>('/api/plantillas-obra')
}
