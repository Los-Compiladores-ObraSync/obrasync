import { useContext } from 'react'
import { BloqueadasContext, type BloqueadasContextValue } from './bloqueadasContext'

/** Cantidad de �rdenes de compra bloqueadas por presupuesto y funci�n para refrescar. */
export function useBloqueadas(): BloqueadasContextValue {
  return useContext(BloqueadasContext)
}
