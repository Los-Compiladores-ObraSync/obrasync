import { useContext } from 'react'
import { AvisosContext, type AvisosContextValue } from './avisosContext'

/** Avisos del flujo de órdenes (#219): lo que hay para hacer y las novedades de cada rol. */
export function useAvisos(): AvisosContextValue {
  return useContext(AvisosContext)
}
