import { useCallback, useEffect, useRef, useState } from 'react'

/**
 * Dictado por voz del chat del asistente (#139).
 *
 * Usa el reconocimiento de voz del propio navegador (Web Speech API), sin librerías ni backend nuevos.
 * Por eso: anda mejor en Chrome y Edge, necesita internet y, en Chrome, el audio lo transcribe un servicio
 * de Google. Esta app no graba, no guarda ni envía el audio a ningún servidor propio: solo recibe el texto.
 * Si más adelante el equipo prefiere mandar el audio al modelo multimodal, se cambia solo este archivo.
 *
 * TypeScript no trae los tipos de esta API, así que se declara acá solo lo que se usa.
 */

interface AlternativaVoz {
  transcript: string
}

interface EventoResultadoVoz {
  results: ArrayLike<ArrayLike<AlternativaVoz>>
}

interface EventoErrorVoz {
  error: string
}

interface ReconocimientoVoz {
  lang: string
  continuous: boolean
  interimResults: boolean
  onresult: ((evento: EventoResultadoVoz) => void) | null
  onerror: ((evento: EventoErrorVoz) => void) | null
  onend: (() => void) | null
  start: () => void
  stop: () => void
  abort: () => void
}

type ConstructorReconocimiento = new () => ReconocimientoVoz

/** Español de Argentina: el capataz habla así y el reconocimiento entiende mejor los modismos. */
export const IDIOMA_DICTADO = 'es-AR'

export const MENSAJE_SIN_SOPORTE =
  'Tu navegador no permite dictar por voz. Probá con Chrome o Edge, o escribí el mensaje.'

const MENSAJE_GENERICO = 'No se pudo usar el micrófono. Podés escribir el mensaje.'

function obtenerConstructor(): ConstructorReconocimiento | null {
  const ventana = window as unknown as {
    SpeechRecognition?: ConstructorReconocimiento
    webkitSpeechRecognition?: ConstructorReconocimiento
  }
  return ventana.SpeechRecognition ?? ventana.webkitSpeechRecognition ?? null
}

/** Traduce el código de error del navegador a una frase que entienda quien está en la obra. */
export function mensajeDeErrorVoz(codigo: string): string | null {
  switch (codigo) {
    case 'aborted':
      return null // lo cortó la propia app (por ejemplo, al enviar): no es un error para mostrar
    case 'not-allowed':
    case 'service-not-allowed':
      return 'No hay permiso para usar el micrófono. Activalo en la configuración del navegador y probá de nuevo.'
    case 'no-speech':
      return 'No te escuché. Probá de nuevo, más cerca del micrófono.'
    case 'audio-capture':
      return 'No se encontró un micrófono. Podés escribir el mensaje.'
    case 'network':
      return 'No hay conexión a internet: el dictado la necesita. Podés escribir el mensaje.'
    default:
      return MENSAJE_GENERICO
  }
}

interface OpcionesVoz {
  /** Se llama justo antes de empezar a escuchar: la pantalla guarda lo que ya había escrito. */
  alEmpezar: () => void
  /** Se llama cada vez que cambia lo que se escuchó. Trae el texto completo del dictado, no solo lo nuevo. */
  alTranscribir: (transcripcion: string) => void
}

export function useReconocimientoVoz({ alEmpezar, alTranscribir }: OpcionesVoz) {
  const [escuchando, setEscuchando] = useState(false)
  const [errorVoz, setErrorVoz] = useState<string | null>(null)
  const reconocimiento = useRef<ReconocimientoVoz | null>(null)
  // El dictado sigue hasta que la persona lo frena. El navegador a veces lo corta solo (por silencio):
  // en ese caso se reinicia, y lo ya dictado se conserva en "previo".
  const frenadoPorUsuario = useRef(false)
  const falloGrave = useRef(false)
  const previo = useRef('')
  const ultimaTranscripcion = useRef('')
  // Siempre las últimas funciones de la pantalla, sin volver a armar el reconocimiento en cada render.
  const funciones = useRef({ alEmpezar, alTranscribir })

  useEffect(() => {
    funciones.current = { alEmpezar, alTranscribir }
  })

  const detener = useCallback(() => {
    frenadoPorUsuario.current = true
    reconocimiento.current?.stop()
  }, [])

  const abortar = useCallback(() => {
    frenadoPorUsuario.current = true
    reconocimiento.current?.abort()
  }, [])

  // Al salir de la pantalla se corta el micrófono.
  useEffect(() => abortar, [abortar])

  const alternar = useCallback(() => {
    if (reconocimiento.current) {
      detener()
      return
    }

    const Constructor = obtenerConstructor()
    if (!Constructor) {
      setErrorVoz(MENSAJE_SIN_SOPORTE)
      return
    }

    setErrorVoz(null)
    frenadoPorUsuario.current = false
    falloGrave.current = false
    previo.current = ''
    ultimaTranscripcion.current = ''
    const nuevo = new Constructor()
    nuevo.lang = IDIOMA_DICTADO
    nuevo.continuous = true
    nuevo.interimResults = true
    nuevo.onresult = (evento) => {
      let transcripcion = ''
      for (let i = 0; i < evento.results.length; i += 1) {
        transcripcion += evento.results[i][0].transcript
      }
      const sesion = transcripcion.trim()
      const total = previo.current === '' ? sesion : sesion === '' ? previo.current : `${previo.current} ${sesion}`
      ultimaTranscripcion.current = total
      funciones.current.alTranscribir(total)
    }
    nuevo.onerror = (evento) => {
      // Un silencio largo no es un error: se sigue escuchando hasta que la persona frene.
      if (evento.error === 'no-speech') return
      if (evento.error !== 'aborted') falloGrave.current = true
      const mensaje = mensajeDeErrorVoz(evento.error)
      if (mensaje) setErrorVoz(mensaje)
    }
    nuevo.onend = () => {
      if (reconocimiento.current === nuevo && !frenadoPorUsuario.current && !falloGrave.current) {
        // El navegador cortó solo (por ejemplo, por una pausa): se reinicia conservando lo dictado.
        previo.current = ultimaTranscripcion.current
        try {
          nuevo.start()
          return
        } catch {
          // Si no se puede reiniciar, se cierra normalmente.
        }
      }
      reconocimiento.current = null
      setEscuchando(false)
    }

    funciones.current.alEmpezar()
    reconocimiento.current = nuevo
    try {
      nuevo.start()
      setEscuchando(true)
    } catch {
      reconocimiento.current = null
      setErrorVoz(MENSAJE_GENERICO)
    }
  }, [detener])

  return { escuchando, errorVoz, alternar, detener }
}
