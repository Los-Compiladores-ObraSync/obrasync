import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useAuth } from '../auth/useAuth'
import { listarRequisiciones } from '../requisiciones/requisicionesService'
import { INTERVALO_REFRESCO_MS, ROLES_QUE_VEN_PENDIENTES } from './configuracionAvisos'
import { PendientesContext } from './pendientesContext'

interface Props {
  children: ReactNode
  /** Solo para pruebas: permite achicar el intervalo de 60 segundos. */
  intervaloMs?: number
}

/** Mantiene al día cuántas requisiciones esperan aprobación (#48).
 *  Pide la lista con ?estado=Pendiente y cuenta; el backend no tiene un
 *  endpoint que devuelva solo la cantidad.
 *  Se actualiza: al montarse (al iniciar sesión), cada intervalo, al volver a la
 *  pestaña y cuando alguien llama a refrescar(). No pide nada con la pestaña oculta. */
export function PendientesProvider({
  children,
  intervaloMs = INTERVALO_REFRESCO_MS,
}: Props) {
  const { usuario } = useAuth()
  const puedeVer = usuario ? ROLES_QUE_VEN_PENDIENTES.includes(usuario.rol) : false
  const [cantidad, setCantidad] = useState(0)

  // Cada vez que cambia "version" el efecto de abajo vuelve a pedir la lista
  // (mismo truco que usa la pantalla de aprobación con su botón "Reintentar").
  const [version, setVersion] = useState(0)
  const refrescar = useCallback(() => setVersion((n) => n + 1), [])

  useEffect(() => {
    if (!puedeVer) return

    let cancelado = false

    async function pedir() {
      try {
        const lista = await listarRequisiciones({ estado: 'Pendiente' })
        if (!cancelado) setCantidad(lista.length)
      } catch {
        // Un error de red no tiene que romper el encabezado: se conserva el último número.
      }
    }

    void pedir()

    const temporizador = window.setInterval(() => {
      if (!document.hidden) void pedir()
    }, intervaloMs)

    function alCambiarVisibilidad() {
      if (!document.hidden) void pedir()
    }
    document.addEventListener('visibilitychange', alCambiarVisibilidad)

    return () => {
      cancelado = true
      window.clearInterval(temporizador)
      document.removeEventListener('visibilitychange', alCambiarVisibilidad)
    }
  }, [puedeVer, intervaloMs, version])

  const valor = useMemo(
    () => ({ cantidad: puedeVer ? cantidad : 0, refrescar }),
    [puedeVer, cantidad, refrescar],
  )

  return <PendientesContext.Provider value={valor}>{children}</PendientesContext.Provider>
}
