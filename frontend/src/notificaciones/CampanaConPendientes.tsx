import { CampanaNotificaciones } from './CampanaNotificaciones'
import { usePendientes } from './usePendientes'

/** La campana del encabezado conectada a la cantidad real de pendientes. */
export function CampanaConPendientes() {
  const { cantidad } = usePendientes()
  return <CampanaNotificaciones cantidad={cantidad} />
}
