import { apiFetch } from '../api/client'

/** Un capataz disponible para asignar a una obra. Es el item de GET /api/usuarios/capataces. */
export interface Capataz {
  id: string
  nombreCompleto: string
}

/**
 * Lista los usuarios con rol Capataz, para elegir uno al crear una obra.
 * Fuente: GET /api/usuarios/capataces (contracts/openapi/usuarios-obras-openapi.json)
 */
export function obtenerCapataces(): Promise<Capataz[]> {
  return apiFetch<Capataz[]>('/api/usuarios/capataces')
}
