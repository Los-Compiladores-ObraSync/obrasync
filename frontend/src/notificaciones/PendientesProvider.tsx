import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useAuth } from '../auth/useAuth'
import { listarRequisiciones } from '../requisiciones/requisicionesService'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import { INTERVALO_REFRESCO_MS, ROLES_QUE_VEN_PENDIENTES } from './configuracionAvisos'
import { PendientesContext } from './pendientesContext'

interface Props {
  children: ReactNode
  /** Solo para pruebas: permite achicar el intervalo de 60 segundos. */
  intervaloMs?: number
}

/** Mantiene al día las requisiciones que esperan aprobación (#48, #123).
 *  Pide la lista con ?estado=Pendiente y cuenta; el backend no tiene un
 *  endpoint que devuelva solo la cantidad. La misma lista la usa el tablero de
 *  pendientes, así no se pide dos veces.
 *  Se actualiza: al montarse (al iniciar sesión), cada intervalo, al volver a la
 *  pestaña y cuando alguien llama a refrescar(). No pide nada con la pestaña oculta. */
export function PendientesProvider({
  children,
  intervaloMs = INTERVALO_REFRESCO_MS,
}: Props) {
  const { usuario } = useAuth()
  const puedeVer = usuario ? ROLES_QUE_VEN_PENDIENTES.includes(usuario.rol) : false
  const [lista, setLista] = useState<RequisicionResponseDto[]>([])
  const [cargado, setCargado] = useState(false)
  const [huboError, setHuboError] = useState(false)

  // Cada vez que cambia "version" el efecto de abajo vuelve a pedir la lista
  // (mismo truco que usa la pantalla de aprobación con su botón "Reintentar").
  const [version, setVersion] = useState(0)
  const refrescar = useCallback(() => {
    setHuboError(false)
    setVersion((n) => n + 1)
  }, [])

  useEffect(() => {
    if (!puedeVer) return

    let cancelado = false

    async function pedir() {
      try {
        const respuesta = await listarRequisiciones({ estado: 'Pendiente' })
        if (cancelado) return
        setLista(respuesta)
        setCargado(true)
        setHuboError(false)
      } catch {
        // Un error de red no tiene que romper el encabezado: se conserva el último número.
        // Se avisa con huboError para que el tablero pueda ofrecer "Reintentar".
        if (!cancelado) setHuboError(true)
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
    () => ({
      cantidad: puedeVer ? lista.length : 0,
      pendientes: puedeVer ? lista : [],
      cargado: puedeVer && cargado,
      huboError: puedeVer && huboError,
      refrescar,
    }),
    [puedeVer, lista, cargado, huboError, refrescar],
  )

  return <PendientesContext.Provider value={valor}>{children}</PendientesContext.Provider>
}
