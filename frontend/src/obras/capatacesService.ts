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

/** Cuerpo de POST /api/usuarios/registro (RegisterRequestDto del contrato). */
export interface CrearCapatazDto {
  nombreCompleto: string
  email: string
  /** Contraseña inicial: la define quien lo crea y se la pasa al capataz por su cuenta. */
  password: string
}

/** Respuesta de POST /api/usuarios/registro (AuthResponseDto). Solo usamos el id y el nombre. */
export interface UsuarioCreado {
  id: string
  nombreCompleto: string
}

/**
 * Crea un usuario con rol Capataz (el rol va fijo). Con token de Comprador o Admin el backend lo
 * permite (#117). OJO: la respuesta trae un token del usuario nuevo; acá NO se usa, para no
 * reemplazar la sesión de quien lo creó.
 * Fuente: POST /api/usuarios/registro (contracts/openapi/usuarios-obras-openapi.json)
 */
export function crearCapataz(dto: CrearCapatazDto): Promise<UsuarioCreado> {
  return apiFetch<UsuarioCreado>('/api/usuarios/registro', {
    method: 'POST',
    body: JSON.stringify({ ...dto, rol: 'Capataz' }),
  })
}
