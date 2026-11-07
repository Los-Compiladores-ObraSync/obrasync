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

/** Cuerpo de PUT /api/Obras/{id}. Es el ActualizarObraDto del contrato (#118). No incluye las etapas. */
export interface ActualizarObraDto {
  nombre: string
  ubicacion: string
  /** Fecha y hora en formato ISO 8601 (date-time). */
  fechaInicio: string
  capatazId: string
}

/** Edita una obra (Admin y Comprador). Responde 400, 403, 404 (no existe) o 409 (nombre repetido). */
export function actualizarObra(id: string, dto: ActualizarObraDto): Promise<unknown> {
  return apiFetch<unknown>(`/api/obras/${encodeURIComponent(id)}`, {
    method: 'PUT',
    body: JSON.stringify(dto),
  })
}

/**
 * Da de baja una obra (Admin y Comprador). Es una baja LÓGICA: la obra deja de aparecer en el
 * listado, pero no se borra (otros servicios guardan su id). Responde 204 sin cuerpo, o 403 / 404.
 */
export function darDeBajaObra(id: string): Promise<void> {
  return apiFetch<void>(`/api/obras/${encodeURIComponent(id)}`, { method: 'DELETE' })
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
