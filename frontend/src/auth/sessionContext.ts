import { createContext } from 'react'

// ── Tipos públicos ────────────────────────────────────────────────────────────

/** Datos del usuario que se guardan en sesión. */
export interface SesionUsuario {
  id: string
  nombreCompleto: string
  email: string
  /** Rol activo del usuario: Capataz, Comprador, Proveedor o Admin.
   *  El backend puede devolver varios roles, pero por decisión de equipo
   *  usamos el primero (roles[0]) de AuthResponseDto. */
  rol: string
}

export interface AuthContextValue {
  /** null = no hay sesión activa */
  usuario: SesionUsuario | null
  /** Guarda el token y el usuario en localStorage y actualiza el estado. */
  login: (usuario: SesionUsuario, token: string) => void
  /** Borra la sesión de localStorage y limpia el estado. */
  logout: () => void
}

// ── Almacenamiento ────────────────────────────────────────────────────────────

export const STORAGE_KEY = 'obrasync_session'

export interface StoredSession {
  usuario: SesionUsuario
  token: string
}

export function leerSesionGuardada(): StoredSession | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as StoredSession) : null
  } catch {
    // Si localStorage devuelve un JSON corrupto, empezamos sin sesión.
    return null
  }
}

// ── Objeto de contexto ────────────────────────────────────────────────────────

export const AuthContext = createContext<AuthContextValue | null>(null)
