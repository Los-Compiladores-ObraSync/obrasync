import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { traerTodas } from '../api/paginacion'
import { useAuth } from '../auth/useAuth'
import {
  listarCotizaciones,
  listarLicitaciones,
  listarOrdenesCompra,
  obtenerMisCotizaciones,
  obtenerMiReputacion,
} from '../licitaciones/licitacionesService'
import { obtenerOrdenesPendientes } from '../recepciones/recepcionesService'
import { listarRequisiciones } from '../requisiciones/requisicionesService'
import {
  avisosDeFlujo,
  claveVistosFlujo,
  rolDeAvisosDeFlujo,
  vistosInicialesDeFlujo,
  type Aviso,
  type DatosAvisos,
  type RolDeAvisos,
} from './avisosFlujo'
import { AvisosContext } from './avisosContext'
import {
  INTERVALO_REFRESCO_MS,
  MAX_LICITACIONES_CONSULTADAS_PARA_COTIZACIONES,
} from './configuracionAvisos'
import { guardarVistos, leerVistos, type Vistos } from './novedades'

interface Props {
  children: ReactNode
  /** Solo para pruebas: permite achicar el intervalo de 30 segundos. */
  intervaloMs?: number
}

interface Resultado {
  /** Solo las listas que se pudieron pedir esta vez. */
  datos: DatosAvisos
  /** false si alguna no se pudo pedir. */
  completo: boolean
}

/** Pide al backend las listas que cada rol necesita para armar sus avisos. Cada lista falla por separado:
 *  si una no responde, las demás se actualizan igual y de la que falló se conserva lo último que se supo. */
async function pedirDatos(rol: RolDeAvisos): Promise<Resultado> {
  const datos: DatosAvisos = {}
  let completo = true
  const fallo = () => {
    completo = false
  }

  if (rol === 'capataz') {
    await obtenerOrdenesPendientes().then((l) => (datos.ordenesPendientes = l), fallo)
  } else if (rol === 'proveedor') {
    await Promise.all([
      listarOrdenesCompra().then((l) => (datos.ordenesCompra = l), fallo),
      obtenerMisCotizaciones().then((l) => (datos.misCotizaciones = l), fallo),
      obtenerMiReputacion().then((r) => (datos.reputacion = r), fallo),
    ])
  } else {
    await Promise.all([
      traerTodas((pagina) => listarRequisiciones({ estado: 'Aprobada' }, pagina)).then(
        (l) => (datos.requisicionesAprobadas = l),
        fallo,
      ),
      listarLicitaciones().then((l) => (datos.licitaciones = l), fallo),
      listarOrdenesCompra().then((l) => (datos.ordenesCompra = l), fallo),
    ])

    // Cotizaciones de las licitaciones abiertas que vencen primero (el servicio no informa la cantidad en la lista).
    if (datos.licitaciones) {
      const abiertas = datos.licitaciones
        .filter((l) => l.estado === 'Abierta')
        .sort((a, b) => Date.parse(a.fechaLimiteCotizacion) - Date.parse(b.fechaLimiteCotizacion))
        .slice(0, MAX_LICITACIONES_CONSULTADAS_PARA_COTIZACIONES)
      const cantidades: Record<string, number> = {}
      await Promise.all(
        abiertas.map((lic) =>
          listarCotizaciones(lic.licitacionId).then(
            (cotizaciones) => {
              cantidades[lic.licitacionId] = cotizaciones.length
            },
            fallo,
          ),
        ),
      )
      datos.cotizacionesPorLicitacion = cantidades
    }
  }

  return { datos, completo }
}

interface Memoria {
  clave: string | null
  datos: DatosAvisos
  /** null = primera vez en este navegador y todavía no se pudo establecer qué era viejo. */
  vistos: Vistos | null
}

interface Estado {
  clave: string
  datos: DatosAvisos
  vistos: Vistos | null
  /** Momento de la última consulta: sirve para saber si una licitación está por vencer. */
  ahora: number
}

/** Mantiene al día los avisos del flujo Requisición → Licitación → Orden de Compra → Recepción (#219).
 *  Consulta las listas que ya existen en el backend (no hay un servicio de notificaciones), al montarse,
 *  cada intervalo, al volver a la pestaña. No pide nada con la pestaña oculta.
 *  "Para hacer" se calcula desde los datos; las "Novedades" se recuerdan como vistas en localStorage. */
export function AvisosProvider({ children, intervaloMs = INTERVALO_REFRESCO_MS }: Props) {
  const { usuario } = useAuth()
  const rol = rolDeAvisosDeFlujo(usuario?.rol)
  const usuarioId = usuario?.id
  const clave = rol && usuarioId ? claveVistosFlujo(rol, usuarioId) : null

  const memoria = useRef<Memoria>({ clave: null, datos: {}, vistos: null })
  const [estado, setEstado] = useState<Estado | null>(null)
  // La consulta del efecto, para poder pedirla desde afuera (refrescar) sin esperar al intervalo.
  const pedirRef = useRef<(() => Promise<void>) | null>(null)
  // Cuándo empezó la última consulta (para no repetirla de más al abrir la campana).
  const ultimaConsultaRef = useRef(0)

  useEffect(() => {
    if (!rol || !clave) return
    const rolActual = rol
    const claveActual = clave

    let cancelado = false
    // Cada consulta lleva un número: si una vieja y lenta termina después de una más nueva, se descarta
    // para que no pise datos más recientes (ej.: volver a mostrar "despachá" una orden ya despachada).
    let ultimaSecuencia = 0
    if (memoria.current.clave !== claveActual) {
      memoria.current = { clave: claveActual, datos: {}, vistos: leerVistos(claveActual) }
    }

    async function pedir() {
      const secuencia = ++ultimaSecuencia
      ultimaConsultaRef.current = Date.now()
      const { datos, completo } = await pedirDatos(rolActual)
      if (cancelado || secuencia !== ultimaSecuencia) return
      const m = memoria.current
      m.datos = { ...m.datos, ...datos }
      const ahora = Date.now()
      // Primera vez: lo que ya había pasado se da por visto. Solo se hace con TODAS las listas a la vista;
      // si alguna falló, las novedades esperan (no se muestran) para no llenar la campana de avisos viejos.
      if (m.vistos === null && completo) {
        m.vistos = vistosInicialesDeFlujo(rolActual, m.datos, ahora)
        guardarVistos(claveActual, m.vistos)
      }
      setEstado({ clave: claveActual, datos: m.datos, vistos: m.vistos, ahora })
    }

    pedirRef.current = pedir
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
      pedirRef.current = null
      window.clearInterval(temporizador)
      document.removeEventListener('visibilitychange', alCambiarVisibilidad)
    }
  }, [rol, clave, intervaloMs])

  const avisos = useMemo<Aviso[]>(() => {
    if (!rol || !clave || !estado || estado.clave !== clave) return []
    return avisosDeFlujo(rol, estado.datos, estado.vistos, estado.ahora)
  }, [rol, clave, estado])

  const paraHacer = useMemo(() => avisos.filter((a) => a.seccion === 'para-hacer'), [avisos])
  const novedades = useMemo(() => avisos.filter((a) => a.seccion === 'novedades'), [avisos])

  const marcarVistas = useCallback(
    (cuales?: Aviso[]) => {
      const m = memoria.current
      if (!m.clave || m.vistos === null) return
      const vistos: Vistos = { ...m.vistos }
      for (const aviso of cuales ?? novedades) {
        if (aviso.seccion === 'novedades') vistos[aviso.id] = aviso.marca
      }
      m.vistos = vistos
      guardarVistos(m.clave, vistos)
      setEstado((actual) => (actual && actual.clave === m.clave ? { ...actual, vistos } : actual))
    },
    [novedades],
  )

  const refrescar = useCallback((esperaMinimaMs?: number) => {
    if (esperaMinimaMs && Date.now() - ultimaConsultaRef.current < esperaMinimaMs) return
    void pedirRef.current?.()
  }, [])

  const datos = estado && estado.clave === clave ? estado.datos : null

  const valor = useMemo(
    () => ({
      cantidad: paraHacer.length + novedades.length,
      paraHacer,
      novedades,
      marcarVistas,
      refrescar,
      datos,
    }),
    [paraHacer, novedades, marcarVistas, refrescar, datos],
  )

  return <AvisosContext.Provider value={valor}>{children}</AvisosContext.Provider>
}
