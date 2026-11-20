import { useContext } from 'react'
import { NovedadesContext, type NovedadesContextValue } from './novedadesContext'

/** Avisos propios del Capataz (requisición aprobada o rechazada) y del Proveedor (licitación nueva). */
export function useNovedades(): NovedadesContextValue {
  return useContext(NovedadesContext)
}
