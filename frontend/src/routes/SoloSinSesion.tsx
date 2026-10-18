import { Navigate, Outlet } from 'react-router'
import { useAuth } from '../auth/useAuth'

/** Es la inversa de RutaProtegida: el login solo se muestra si NO hay sesión.
 *  Si ya hay sesión (o se acaba de iniciar), manda al inicio. */
export function SoloSinSesion() {
  const { usuario } = useAuth()

  if (usuario) {
    return <Navigate to="/" replace />
  }

  return <Outlet />
}
