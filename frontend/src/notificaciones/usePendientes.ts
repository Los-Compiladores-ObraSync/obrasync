import { useContext } from 'react'
import { PendientesContext, type PendientesContextValue } from './pendientesContext'

/** Cantidad de requisiciones pendientes de aprobar y una función para refrescarla. */
export function usePendientes(): PendientesContextValue {
  return useContext(PendientesContext)
}
