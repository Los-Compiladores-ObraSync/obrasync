import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useAuth } from '../auth/useAuth'
import { listarLicitaciones } from '../licitaciones/licitacionesService'
import { listarRequisiciones } from '../requisiciones/requisicionesService'
import { INTERVALO_REFRESCO_MS } from './configuracionAvisos'
import {
  claveVistos,
  guardarVistos,
  leerVistos,
  novedadesDeLicitaciones,
  novedadesDeRequisiciones,
  tipoDeAvisosParaRol,
  vistosIniciales,
  type Novedad,
  type Vistos,
} from './novedades'
import { NovedadesContext } from './novedadesContext'

interface Props {
  children: ReactNode
  /** Solo para pruebas: permite achicar el intervalo de 60 segundos. */
  intervaloMs?: number
}

/** Avisos propios de la campana para el Capataz y el Proveedor (#163).
 *  - Capataz: una requisición suya pasó a Aprobada o Rechazada desde la última vez que miró
 *    (GET /api/requisiciones; #155 hace que el servicio le devuelva solo las suyas).
 *  - Proveedor: hay una licitación abierta que todavía no cotizó (GET /api/licitaciones).
 *  Son consultas periódicas, como los demás avisos: no hay notificaciones push, así que no es
 *  instantáneo. Lo ya visto se recuerda en este navegador (localStorage), sin guardar nada en el servidor.
 *  Se consulta al montarse, cada intervalo y al volver a la pestaña; no se consulta con la pestaña oculta. */
export function NovedadesProvider({ children, intervaloMs = INTERVALO_REFRESCO_MS }: Props) {
  const { usuario } = useAuth()
  const tipo = tipoDeAvisosParaRol(usuario?.rol)
  const usuarioId = usuario?.id
  const clave = tipo && usuarioId ? claveVistos(tipo, usuarioId) : null

  // Los datos llevan la clave con la que se pidieron: si cambia la persona, no se mezclan.
  const [datos, setDatos] = useState<{
    clave: string
    novedades: Novedad[]
    vistos: Vistos
  } | null>(null)

  useEffect(() => {
    if (!clave || !tipo) return

    let cancelado = false

    async function pedir() {
      if (!clave || !tipo) return
      try {
        let vistos: Vistos
        let novedades: Novedad[]
        if (tipo === 'capataz') {
          const { items } = await listarRequisiciones()
          let guardados = leerVistos(clave)
          if (guardados === null) {
            // Primera vez en este navegador: lo que ya estaba resuelto se da por visto.
            guardados = vistosIniciales(items)
            guardarVistos(clave, guardados)
          }
          vistos = guardados
          novedades = novedadesDeRequisiciones(items, vistos)
        } else {
          const licitaciones = await listarLicitaciones()
          vistos = leerVistos(clave) ?? {}
          novedades = novedadesDeLicitaciones(licitaciones, vistos)
        }
        if (!cancelado) setDatos({ clave, novedades, vistos })
      } catch {
        // Un error de red no tiene que romper el encabezado: se conservan los últimos avisos.
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
  }, [clave, tipo, intervaloMs])

  const vigentes = datos && datos.clave === clave ? datos : null

  const marcarVistas = useCallback(
    (cuales?: Novedad[]) => {
      if (!clave || !vigentes) return
      const aMarcar = cuales ?? vigentes.novedades
      const vistos: Vistos = { ...vigentes.vistos }
      for (const novedad of aMarcar) vistos[novedad.id] = novedad.marca
      guardarVistos(clave, vistos)
      const marcadas = new Set(aMarcar.map((n) => n.id))
      setDatos({
        clave,
        vistos,
        novedades: vigentes.novedades.filter((n) => !marcadas.has(n.id)),
      })
    },
    [clave, vigentes],
  )

  const valor = useMemo(
    () => ({
      cantidad: vigentes?.novedades.length ?? 0,
      novedades: vigentes?.novedades ?? [],
      marcarVistas,
    }),
    [vigentes, marcarVistas],
  )

  return <NovedadesContext.Provider value={valor}>{children}</NovedadesContext.Provider>
}
