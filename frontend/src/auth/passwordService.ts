import { apiFetch } from '../api/client'

// Fuente: contracts/openapi/usuarios-obras-openapi.json (tag Auth)

/** Respuesta de los endpoints de contraseña (MensajeResponseDto del contrato). */
export interface MensajeResponseDto {
  mensaje?: string | null
}

/** Cuerpo de POST /api/usuarios/password/restablecer (RestablecerPasswordRequestDto del contrato). */
export interface RestablecerPasswordRequest {
  email: string
  /** Token de recuperación que viene en el enlace. */
  token: string
  /** Mínimo 6 caracteres. */
  nuevaPassword: string
}

/** Cuerpo de POST /api/usuarios/cambiar-password (CambiarPasswordRequestDto del contrato). */
export interface CambiarPasswordRequest {
  passwordActual: string
  /** Mínimo 6 caracteres y distinta de la actual. */
  nuevaPassword: string
  /** Tiene que coincidir con nuevaPassword. */
  confirmarPassword: string
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

/**
 * Aplica la nueva contraseña con el token del enlace.
 * Fuente: POST /api/usuarios/password/restablecer (público, sin JWT).
 *
 * Lanza ApiError con status 400 si el token es inválido o venció, o si la contraseña no
 * cumple los requisitos (el contrato no distingue los dos casos).
 */
export function restablecerPassword(datos: RestablecerPasswordRequest): Promise<MensajeResponseDto> {
  return apiFetch<MensajeResponseDto>('/api/usuarios/password/restablecer', {
    method: 'POST',
    body: JSON.stringify(datos),
  })
}

/**
 * Cambia la contraseña del usuario con la sesión iniciada.
 * Fuente: POST /api/usuarios/cambiar-password (requiere JWT, cualquier rol).
 *
 * Lanza ApiError con status 400 si la contraseña actual es incorrecta, si la nueva es igual a
 * la actual, si la confirmación no coincide o si no cumple la política. Con 401 (token vencido)
 * apiFetch ya cierra la sesión solo.
 */
export function cambiarPassword(datos: CambiarPasswordRequest): Promise<MensajeResponseDto> {
  return apiFetch<MensajeResponseDto>('/api/usuarios/cambiar-password', {
    method: 'POST',
    body: JSON.stringify(datos),
  })
}
