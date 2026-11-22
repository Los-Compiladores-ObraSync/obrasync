import { useCallback, useEffect, useRef, useState } from 'react'
import { useAuth } from '../auth/useAuth'
import { estaEnModoSimulado } from '../mocks/modoSimulado'
import { CICLOS_ENTRE_CONSULTAS_DE_PROBLEMA, INTERVALO_RESPALDO_MS } from './chatConfig'
import { mensajeDeErrorChat, unirMensajes, validarMensaje } from './chatMensajes'
import {
  enviarMensaje,
  listarMensajes,
  marcarLeido,
  obtenerProblemaDeOrden,
  resolverProblema,
} from './chatService'
import type { EstadoEnVivo, EstadoProblema, MensajeChatOrdenDto } from './chatTipos'
import { abrirConexionChat } from './conexionChat'

interface Opciones {
  ordenCompraId: string
  /** Cómo está la entrega con problema según la lista de órdenes, hasta que el tiempo real o la consulta lo actualicen. */
  problemaInicial: EstadoProblema
  /** Se llama cuando la conversación quedó marcada como leída (para bajar el contador de no leídos). */
  alLeer?: () => void
}

export interface EstadoChat {
  mensajes: MensajeChatOrdenDto[]
  cargando: boolean
  errorCarga: string | null
  /** 'en-vivo', 'conectando' o 'sin-conexion' (en ese caso se consulta cada 10 segundos). */
  enVivo: EstadoEnVivo
  problema: EstadoProblema
  /** Hasta cuándo leyó la otra parte, si avisó en vivo. Los mensajes propios anteriores a esa fecha se ven como "Visto". */
  vistoHasta: string | null
  /** Envía un mensaje. Devuelve el texto del error, o null si salió bien. */
  enviar: (texto: string) => Promise<string | null>
  /** Marca el problema como resuelto (solo Compras). Devuelve el texto del error, o null si salió bien. */
  resolver: () => Promise<string | null>
  reintentar: () => void
}

/**
 * La conversación de una orden (#225): historial por REST, novedades en vivo por SignalR y, si la conexión en vivo
 * no está, consulta cada 10 segundos. Al abrirse y al llegar un mensaje de la otra parte marca la conversación
 * como leída. El mismo mensaje puede llegar por varios caminos: se junta por id.
 */
export function useChatOrden({ ordenCompraId, problemaInicial, alLeer }: Opciones): EstadoChat {
  const { usuario } = useAuth()
  const usuarioId = usuario?.id

  const [mensajes, setMensajes] = useState<MensajeChatOrdenDto[]>([])
  const [cargando, setCargando] = useState(true)
  const [errorCarga, setErrorCarga] = useState<string | null>(null)
  // En modo simulado (mocks) no hay hub: se arranca directo con la consulta de respaldo.
  const [enVivo, setEnVivo] = useState<EstadoEnVivo>(estaEnModoSimulado() ? 'sin-conexion' : 'conectando')
  const [problema, setProblema] = useState<EstadoProblema>(problemaInicial)
  const [vistoHasta, setVistoHasta] = useState<string | null>(null)
  // Cada vez que cambia "intento" se vuelve a pedir el historial ("Reintentar").
  const [intento, setIntento] = useState(0)

  const alLeerRef = useRef(alLeer)
  useEffect(() => {
    alLeerRef.current = alLeer
  })

  const leer = useCallback(async () => {
    try {
      await marcarLeido(ordenCompraId)
      alLeerRef.current?.()
    } catch {
      // No es grave: el contador se corrige en la próxima consulta.
    }
  }, [ordenCompraId])

  // Historial al abrir (y al reintentar).
  useEffect(() => {
    let cancelado = false
    listarMensajes(ordenCompraId)
      .then((lista) => {
        if (cancelado) return
        setMensajes((actuales) => unirMensajes(actuales, lista))
        setErrorCarga(null)
        setCargando(false)
        void leer()
      })
      .catch((err: unknown) => {
        if (cancelado) return
        setErrorCarga(mensajeDeErrorChat(err))
        setCargando(false)
      })
    return () => {
      cancelado = true
    }
  }, [ordenCompraId, intento, leer])

  const sincronizar = useCallback(
    async (conProblema: boolean) => {
      try {
        const lista = await listarMensajes(ordenCompraId)
        setMensajes((actuales) => unirMensajes(actuales, lista))
      } catch {
        // En el respaldo el próximo ciclo vuelve a intentar.
      }
      if (!conProblema) return
      try {
        const estado = await obtenerProblemaDeOrden(ordenCompraId)
        if (estado) setProblema(estado)
      } catch {
        // Igual que arriba.
      }
    },
    [ordenCompraId],
  )

  // Tiempo real.
  useEffect(() => {
    if (estaEnModoSimulado()) return
    const conexion = abrirConexionChat(ordenCompraId, {
      onMensaje: (mensaje) => {
        setMensajes((actuales) => unirMensajes(actuales, [mensaje]))
        if (mensaje.autorId !== usuarioId) void leer()
      },
      onLectura: (lectura) => {
        if (lectura.usuarioId !== usuarioId) setVistoHasta(lectura.ultimaLecturaEn)
      },
      onProblema: (estado) =>
        setProblema({ abierto: estado.tieneProblemaAbierto, motivo: estado.motivoProblemaAbierto ?? null }),
      onEstado: (estado) => {
        setEnVivo(estado)
        // Al (re)conectar se pone todo al día una vez, por lo que pudo pasar mientras no había tiempo real.
        if (estado === 'en-vivo') void sincronizar(true)
      },
    })
    return () => conexion.cerrar()
  }, [ordenCompraId, usuarioId, leer, sincronizar])

  // Respaldo: sin conexión en vivo se consulta cada 10 segundos.
  useEffect(() => {
    if (enVivo === 'en-vivo') return
    let ciclos = 0
    const temporizador = window.setInterval(() => {
      ciclos += 1
      void sincronizar(ciclos % CICLOS_ENTRE_CONSULTAS_DE_PROBLEMA === 0)
    }, INTERVALO_RESPALDO_MS)
    return () => window.clearInterval(temporizador)
  }, [enVivo, sincronizar])

  const enviar = useCallback(
    async (texto: string): Promise<string | null> => {
      const error = validarMensaje(texto)
      if (error) return error
      try {
        const mensaje = await enviarMensaje(ordenCompraId, texto.trim())
        setMensajes((actuales) => unirMensajes(actuales, [mensaje]))
        return null
      } catch (err) {
        return mensajeDeErrorChat(err)
      }
    },
    [ordenCompraId],
  )

  const resolver = useCallback(async (): Promise<string | null> => {
    try {
      const estado = await resolverProblema(ordenCompraId)
      setProblema({ abierto: estado.tieneProblemaAbierto, motivo: estado.motivoProblemaAbierto ?? null })
      return null
    } catch (err) {
      return mensajeDeErrorChat(err)
    }
  }, [ordenCompraId])

  const reintentar = useCallback(() => {
    setErrorCarga(null)
    setCargando(true)
    setIntento((n) => n + 1)
  }, [])

  return { mensajes, cargando, errorCarga, enVivo, problema, vistoHasta, enviar, resolver, reintentar }
}
