import { apiFetch } from '../api/client'

// Fuente: contracts/openapi/usuarios-obras-openapi.json (tag Auth)

/** Respuesta de los endpoints de contraseña (MensajeResponseDto del contrato). */
export interface MensajeResponseDto {
  mensaje?: string | null
}

/**
 * Pide el enlace para restablecer la contraseña.
 * Fuente: POST /api/usuarios/password/recuperar (público, sin JWT).
 *
 * El backend responde siempre 200 con lo mismo, exista o no la cuenta, para no revelar qué
 * emails están registrados. Por eso la pantalla muestra su propio mensaje neutro y no
 * interpreta la respuesta. Lanza ApiError con status 400 si el email viene mal formado.
 */
export function solicitarRecuperacionPassword(email: string): Promise<MensajeResponseDto> {
  return apiFetch<MensajeResponseDto>('/api/usuarios/password/recuperar', {
    method: 'POST',
    body: JSON.stringify({ email }),
  })
}
