import { apiFetch } from '../api/client'
import type { SesionUsuario } from './sessionContext'

interface AuthResponseDto {
  id: string
  nombreCompleto: string
  email: string
  token: string
  roles: string[]
}

export interface DatosRegistroProveedor {
  nombreCompleto: string
  email: string
  password: string
}

export async function registrarProveedor(
  datos: DatosRegistroProveedor,
): Promise<{ usuario: SesionUsuario; token: string }> {
  const respuesta = await apiFetch<AuthResponseDto>('/api/usuarios', {
    method: 'POST',
    body: JSON.stringify({ ...datos, rol: 'Proveedor' }),
  })

  return {
    usuario: {
      id: respuesta.id,
      nombreCompleto: respuesta.nombreCompleto,
      email: respuesta.email,
      rol: respuesta.roles[0] ?? 'Desconocido',
    },
    token: respuesta.token,
  }
}
