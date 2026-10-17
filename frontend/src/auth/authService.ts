import { apiFetch } from '../api/client'
import type { SesionUsuario } from './sessionContext'

// ── Tipos que reflejan el contrato OpenAPI ────────────────────────────────────
// Fuente: contracts/openapi/usuarios-obras-openapi.json

interface AuthResponseDto {
  id: string
  nombreCompleto: string
  email: string
  token: string
  /** El backend puede devolver varios roles; por decisión de equipo usamos roles[0]. */
  roles: string[]
}

export interface LoginCredenciales {
  email: string
  password: string
}

// ── Función de login ──────────────────────────────────────────────────────────

/**
 * Llama a POST /api/usuarios/login y devuelve el usuario y el token.
 *
 * Lanza ApiError si:
 *   - status 401 → credenciales inválidas (el componente lo atrapa y muestra el mensaje).
 *   - status 400 → request mal formado (no debería ocurrir con el formulario validado).
 *   - cualquier otro error de red → ApiError genérico.
 *
 * NO guarda nada en localStorage ni llama a setToken: eso lo hace AuthContext.login()
 * para mantener la responsabilidad de estado en un solo lugar.
 */
export async function loginApi(
  credenciales: LoginCredenciales,
): Promise<{ usuario: SesionUsuario; token: string }> {
  const data = await apiFetch<AuthResponseDto>('/api/usuarios/login', {
    method: 'POST',
    body: JSON.stringify(credenciales),
  })

  const usuario: SesionUsuario = {
    id: data.id,
    nombreCompleto: data.nombreCompleto,
    email: data.email,
    rol: data.roles[0] ?? 'Desconocido',
  }

  return { usuario, token: data.token }
}
