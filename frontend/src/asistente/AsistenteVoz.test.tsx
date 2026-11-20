import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { AsistenteChatPage } from './AsistenteChatPage'
import { IDIOMA_DICTADO, MENSAJE_SIN_SOPORTE } from './useReconocimientoVoz'

/** Reconocimiento de voz simulado: no usa el micrófono; el test "dicta" llamando a emitir(). */
class FalsoReconocimiento {
  static instancias: FalsoReconocimiento[] = []
  lang = ''
  continuous = true
  interimResults = false
  onresult: ((evento: { results: { transcript: string }[][] }) => void) | null = null
  onerror: ((evento: { error: string }) => void) | null = null
  onend: (() => void) | null = null
  iniciado = false

  constructor() {
    FalsoReconocimiento.instancias.push(this)
  }

  start() {
    this.iniciado = true
  }

  stop() {
    this.iniciado = false
    this.onend?.()
  }

  abort() {
    this.iniciado = false
    this.onend?.()
  }

  emitir(texto: string) {
    this.onresult?.({ results: [[{ transcript: texto }]] })
  }

  fallar(codigo: string) {
    // Igual que el navegador: al fallar, el reconocimiento termina (y la app decide si lo reinicia).
    this.iniciado = false
    this.onerror?.({ error: codigo })
    this.onend?.()
  }
}

function ultimaInstancia(): FalsoReconocimiento {
  const instancia = FalsoReconocimiento.instancias.at(-1)
  if (!instancia) throw new Error('El micrófono no llegó a iniciarse')
  return instancia
}

function renderPagina() {
  return render(
    <MemoryRouter>
      <AsistenteChatPage />
    </MemoryRouter>,
  )
}

describe('AsistenteChatPage: entrada por voz (#139)', () => {
  beforeEach(() => {
    FalsoReconocimiento.instancias = []
    Object.assign(window, { SpeechRecognition: FalsoReconocimiento })
  })

  afterEach(() => {
    Reflect.deleteProperty(window, 'SpeechRecognition')
  })

  it('el dictado aparece en la caja para revisarlo y NO se envía solo', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.click(screen.getByRole('button', { name: 'Dictar por voz' }))

    const reconocimiento = ultimaInstancia()
    expect(reconocimiento.lang).toBe(IDIOMA_DICTADO)
    // Sigue escuchando hasta que la persona lo frene (no corta en la primera pausa).
    expect(reconocimiento.continuous).toBe(true)
    expect(reconocimiento.iniciado).toBe(true)
    expect(screen.getByRole('button', { name: 'Dejar de dictar' })).toHaveAttribute('aria-pressed', 'true')

    act(() => reconocimiento.emitir('necesito cinco bolsas de cemento'))

    expect(screen.getByLabelText('Escribí tu mensaje')).toHaveValue('necesito cinco bolsas de cemento')
    // Sigue en la pantalla de inicio: no se mandó ningún mensaje.
    expect(screen.getByText('¿En qué te ayudo?')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Enviar' })).toBeEnabled()
  })

  it('lo dictado se agrega a lo que ya estaba escrito', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.type(screen.getByLabelText('Escribí tu mensaje'), 'Hola,')
    await user.click(screen.getByRole('button', { name: 'Dictar por voz' }))
    act(() => ultimaInstancia().emitir('quiero pedir arena'))

    expect(screen.getByLabelText('Escribí tu mensaje')).toHaveValue('Hola, quiero pedir arena')
  })

  it('al volver a tocar el micrófono se deja de escuchar y se puede editar el texto', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.click(screen.getByRole('button', { name: 'Dictar por voz' }))
    act(() => ultimaInstancia().emitir('diez bolsas'))
    await user.click(screen.getByRole('button', { name: 'Dejar de dictar' }))

    expect(ultimaInstancia().iniciado).toBe(false)
    expect(screen.getByRole('button', { name: 'Dictar por voz' })).toHaveAttribute('aria-pressed', 'false')
    await user.type(screen.getByLabelText('Escribí tu mensaje'), ' de cal')
    expect(screen.getByLabelText('Escribí tu mensaje')).toHaveValue('diez bolsas de cal')
  })

  it('sin permiso de micrófono muestra un aviso claro y el chat sigue funcionando por escrito', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.click(screen.getByRole('button', { name: 'Dictar por voz' }))
    act(() => ultimaInstancia().fallar('not-allowed'))

    expect(await screen.findByRole('alert')).toHaveTextContent('No hay permiso para usar el micrófono')
    await user.type(screen.getByLabelText('Escribí tu mensaje'), 'sigo escribiendo')
    expect(screen.getByRole('button', { name: 'Enviar' })).toBeEnabled()
  })

  it('sin internet avisa que el dictado la necesita', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.click(screen.getByRole('button', { name: 'Dictar por voz' }))
    act(() => ultimaInstancia().fallar('network'))

    expect(await screen.findByRole('alert')).toHaveTextContent('No hay conexión a internet')
  })

  it('si el navegador corta por una pausa, sigue escuchando y conserva lo dictado', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.click(screen.getByRole('button', { name: 'Dictar por voz' }))
    const reconocimiento = ultimaInstancia()
    act(() => reconocimiento.emitir('diez bolsas'))
    // El navegador corta por silencio: no es un error y se reinicia solo.
    act(() => reconocimiento.fallar('no-speech'))

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(reconocimiento.iniciado).toBe(true)
    expect(screen.getByRole('button', { name: 'Dejar de dictar' })).toBeInTheDocument()

    act(() => reconocimiento.emitir('de cal'))
    expect(screen.getByLabelText('Escribí tu mensaje')).toHaveValue('diez bolsas de cal')
  })

  it('un error grave corta el dictado y no se reinicia en bucle', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.click(screen.getByRole('button', { name: 'Dictar por voz' }))
    const reconocimiento = ultimaInstancia()
    act(() => reconocimiento.fallar('audio-capture'))

    expect(await screen.findByRole('alert')).toHaveTextContent('No se encontró un micrófono')
    expect(reconocimiento.iniciado).toBe(false)
    expect(screen.getByRole('button', { name: 'Dictar por voz' })).toHaveAttribute('aria-pressed', 'false')
  })
})

describe('AsistenteChatPage: navegador sin dictado por voz (#139)', () => {
  beforeEach(() => {
    Reflect.deleteProperty(window, 'SpeechRecognition')
    Reflect.deleteProperty(window, 'webkitSpeechRecognition')
  })

  it('avisa que el navegador no lo permite y el chat sigue andando', async () => {
    const user = userEvent.setup()
    renderPagina()

    await user.click(screen.getByRole('button', { name: 'Dictar por voz' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(MENSAJE_SIN_SOPORTE)
    await user.type(screen.getByLabelText('Escribí tu mensaje'), 'escribo igual')
    expect(screen.getByRole('button', { name: 'Enviar' })).toBeEnabled()
  })
})
