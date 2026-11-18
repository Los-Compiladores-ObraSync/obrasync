import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useAuth } from '../auth/useAuth'
import { listarOrdenesBloqueadas } from '../presupuesto/presupuestoService'
import type { OrdenCompraBloqueada } from '../presupuesto/presupuestoTipos'
import { BloqueadasContext } from './bloqueadasContext'
import { INTERVALO_REFRESCO_MS, ROLES_QUE_VEN_PENDIENTES } from './configuracionAvisos'

interface Props {
  children: ReactNode
  /** Solo para pruebas: permite achicar el intervalo de 60 segundos. */
  intervaloMs?: number
}

/** Mantiene al día las órdenes de compra bloqueadas por desvío presupuestario (#97).
 *  Se actualiza: al montarse, cada intervalo, al volver a la pestaña y al llamar a refrescar().
 *  No pide nada con la pestaña oculta.
 *  Solo consultan los roles Comprador y Admin. */
export function BloqueadasProvider({
  children,
  intervaloMs = INTERVALO_REFRESCO_MS,
}: Props) {
  const { usuario } = useAuth()
  const puedeVer = usuario ? ROLES_QUE_VEN_PENDIENTES.includes(usuario.rol) : false
  const [lista, setLista] = useState<OrdenCompraBloqueada[]>([])
  const [cargado, setCargado] = useState(false)
  const [huboError, setHuboError] = useState(false)

  const [version, setVersion] = useState(0)
  const refrescar = useCallback(() => {
    setHuboError(false)
    setVersion((n) => n + 1)
  }, [])

  const reconciliarOrdenNoBloqueada = useCallback((ordenCompraId: string) => {
    setLista((actual) => actual.filter((orden) => orden.ordenCompraId !== ordenCompraId))
    setHuboError(false)
    setVersion((n) => n + 1)
  }, [])

  useEffect(() => {
    if (!puedeVer) return

    let cancelado = false

    async function pedir() {
      try {
        const respuesta = await listarOrdenesBloqueadas()
        if (cancelado) return
        setLista(respuesta)
        setCargado(true)
        setHuboError(false)
      } catch {
        // Un error de red no debe romper la aplicación: se conserva el último dato disponible.
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
      bloqueadas: puedeVer ? lista : [],
      // Para roles fuera del contrato, la consulta no aplica y no debe quedar en "cargando".
      cargado: !puedeVer || cargado,
      huboError: puedeVer && huboError,
      refrescar,
      reconciliarOrdenNoBloqueada,
    }),
    [puedeVer, lista, cargado, huboError, refrescar, reconciliarOrdenNoBloqueada],
  )

  return <BloqueadasContext.Provider value={valor}>{children}</BloqueadasContext.Provider>
}
