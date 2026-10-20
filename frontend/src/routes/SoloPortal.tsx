import { Outlet } from 'react-router'
import { useAuth } from '../auth/useAuth'
import { puedeEntrar, type PortalId } from '../portales/portales'
import { SinAccesoPage } from '../pages/SinAccesoPage'

interface SoloPortalProps {
  portalId: PortalId
}

/** Deja entrar a un portal solo a los roles que figuran en la tabla de portales.
 *  Ojo: esto es comodidad de pantalla, NO seguridad. La seguridad real la pone cada
 *  servicio del backend validando el JWT y el rol (tarjeta #10). */
export function SoloPortal({ portalId }: SoloPortalProps) {
  const { usuario } = useAuth()

  if (!usuario || !puedeEntrar(usuario.rol, portalId)) {
    return <SinAccesoPage />
  }

  return <Outlet />
}
