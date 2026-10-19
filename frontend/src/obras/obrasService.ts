import { apiFetch } from '../api/client'

/** Cuerpo de POST /api/obras. Es el CrearObraDto del contrato (contracts/openapi/usuarios-obras-openapi.json). */
export interface CrearObraDto {
  nombre: string
  ubicacion: string
  /** Fecha y hora en formato ISO 8601 (date-time). */
  fechaInicio: string
  /** GUID del usuario con rol Capataz que queda a cargo de la obra. */
  capatazId: string
  /** Nombres de las fases/etapas de la obra, en orden. */
  fasesEtapas: string[]
}

/**
 * Crea una obra. Solo pueden hacerlo los roles Admin y Comprador (si no, el backend responde 403).
 * El contrato dice que responde 200 con un string; todavía no está definido qué contiene
 * (¿el id de la obra?), por eso lo devolvemos tal cual y la pantalla no lo interpreta.
 */
export function crearObra(dto: CrearObraDto): Promise<string> {
  return apiFetch<string>('/api/obras', {
    method: 'POST',
    body: JSON.stringify(dto),
  })
}
