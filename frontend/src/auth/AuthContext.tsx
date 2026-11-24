import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { setOnUnauthorized, setToken } from '../api/client'
import {
  AuthContext,
  leerSesionGuardada,
  STORAGE_KEY,
  type SesionUsuario,
  type StoredSession,
} from './sessionContext'

export function AuthProvider({ children }: { children: ReactNode }) {
  // Inicialización perezosa: se ejecuta una sola vez, antes del primer render.
  const [usuario, setUsuario] = useState<SesionUsuario | null>(() => {
    const guardada = leerSesionGuardada()
    if (guardada) {
      // Restaura el token en memoria para que apiFetch lo adjunte desde el primer pedido.
      setToken(guardada.token)
    }
    return guardada?.usuario ?? null
  })

  const logout = useCallback(() => {
    localStorage.removeItem(STORAGE_KEY)
    setToken(null)
    setUsuario(null)
  }, [])

  const login = useCallback((u: SesionUsuario, token: string) => {
    const sesion: StoredSession = { usuario: u, token }
    localStorage.setItem(STORAGE_KEY, JSON.stringify(sesion))
    setToken(token)
    setUsuario(u)
  }, [])

  // Registra logout como handler global de 401 autenticados.
  // Si cualquier apiFetch recibe 401 mientras hay token en memoria,
  // el cliente lo llama y la sesión se cierra automáticamente.
  useEffect(() => {
    setOnUnauthorized(logout)
    return () => setOnUnauthorized(null)
  }, [logout])

  return (
    <AuthContext.Provider value={{ usuario, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}
