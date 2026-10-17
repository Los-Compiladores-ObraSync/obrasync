import { useContext } from 'react'
import { AuthContext, type AuthContextValue } from './sessionContext'

/** Hook para leer el contexto de autenticación.
 *  Lanzará un error si se usa fuera de <AuthProvider>,
 *  lo que ayuda a detectar bugs de estructura temprano. */
export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) {
    throw new Error('useAuth debe usarse dentro de <AuthProvider>')
  }
  return ctx
}
