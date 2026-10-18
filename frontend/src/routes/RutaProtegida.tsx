import { Navigate, Outlet } from 'react-router'
import { useAuth } from '../auth/useAuth'

/** Deja pasar solo si hay sesión; si no, manda al login.
 *  `replace` reemplaza la entrada del historial para que el botón "atrás"
 *  no te devuelva a una pantalla protegida. */
export function RutaProtegida() {
  const { usuario } = useAuth()

  if (!usuario) {
    return <Navigate to="/login" replace />
  }

  return <Outlet />
}
