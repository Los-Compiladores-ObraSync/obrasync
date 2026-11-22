import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthContext } from '../auth/sessionContext'
import { ChatOrden } from './ChatOrden'
import type { ManejadoresChat } from './conexionChat'
import type { MensajeChatOrdenDto } from './chatTipos'

// La conexión en vivo y el servicio se simulan: acá se prueba la pantalla, no el paquete de SignalR ni la red.
const servicio = vi.hoisted(() => ({
  listarMensajes: vi.fn(),
  enviarMensaje: vi.fn(),
  marcarLeido: vi.fn(),
  resolverProblema: vi.fn(),
  obtenerNoLeidos: vi.fn(),
  obtenerProblemaDeOrden: vi.fn(),
}))
const conexion = vi.hoisted(() => ({
  manejadores: undefined as ManejadoresChat | undefined,
  cerrar: vi.fn(),
}))
vi.mock('./chatService', () => servicio)
vi.mock('./conexionChat', () => ({
  abrirConexionChat: (_id: string, manejadores: ManejadoresChat) => {
    conexion.manejadores = manejadores
    return { cerrar: conexion.cerrar }
  },
}))

const ORDEN = 'aaaaaaaa-0000-0000-0000-000000000001'

function mensaje(id: string, texto: string, extra: Partial<MensajeChatOrdenDto> = {}): MensajeChatOrdenDto {
  return {
    id,
    ordenCompraId: ORDEN,
    autorId: 'u-otro',
    autorNombre: 'Carlos Ruiz',
    autorRol: 'Proveedor',
    esSistema: false,
    texto,
    fechaCreacion: '2026-11-22T10:00:00Z',
    ...extra,
  }
}

function renderChat(rol = 'Comprador', props: Partial<React.ComponentProps<typeof ChatOrden>> = {}) {
  const alLeer = vi.fn()
  const alCambiarProblema = vi.fn()
  const usuario = { id: 'u-yo', nombreCompleto: 'Marta Gómez', email: 'm@x.com', rol }
  render(
    <AuthContext.Provider value={{ usuario, login: () => {}, logout: () => {} }}>
      <ChatOrden
        ordenCompraId={ORDEN}
        codigo="AAAAAAAA"
        noLeidos={0}
        problema={{ abierto: false, motivo: null }}
        alLeer={alLeer}
        alCambiarProblema={alCambiarProblema}
        {...props}
      />
    </AuthContext.Provider>,
  )
  return { alLeer, alCambiarProblema }
}

async function abrir(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: /Abrir el chat de la orden AAAAAAAA/ }))
}

beforeEach(() => {
  conexion.manejadores = undefined
  conexion.cerrar.mockReset()
  servicio.listarMensajes.mockResolvedValue([])
  servicio.enviarMensaje.mockReset()
  servicio.marcarLeido.mockResolvedValue({ ordenCompraId: ORDEN, usuarioId: 'u-yo', ultimaLecturaEn: '2026-11-22T10:01:00Z' })
  servicio.resolverProblema.mockReset()
  servicio.obtenerProblemaDeOrden.mockResolvedValue(null)
})

afterEach(() => {
  vi.useRealTimers()
})

describe('ChatOrden', () => {
  it('muestra el contador de no leídos y, al abrir, avisa que se leyó', async () => {
    const user = userEvent.setup()
    const { alLeer } = renderChat('Comprador', { noLeidos: 3 })

    expect(screen.getByRole('button', { name: 'Abrir el chat de la orden AAAAAAAA, 3 sin leer' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Abrir el chat/ }))

    expect(alLeer).toHaveBeenCalled()
    expect(await screen.findByRole('region', { name: 'Chat de la orden AAAAAAAA' })).toBeInTheDocument()
    expect(servicio.marcarLeido).toHaveBeenCalledWith(ORDEN)
  })

  it('muestra el historial y los mensajes del sistema con otro estilo que los de las personas', async () => {
    servicio.listarMensajes.mockResolvedValue([
      mensaje('1', 'Hola, ¿cuándo llega?'),
      mensaje('2', 'La orden fue despachada.', { autorId: null, autorNombre: 'Sistema', autorRol: 'Sistema', esSistema: true, fechaCreacion: '2026-11-22T10:01:00Z' }),
    ])
    const user = userEvent.setup()
    renderChat()
    await abrir(user)

    const persona = (await screen.findByText('Hola, ¿cuándo llega?')).closest('li')
    const sistema = screen.getByText('La orden fue despachada.').closest('li')
    expect(persona).toHaveAttribute('data-tipo', 'persona')
    expect(sistema).toHaveAttribute('data-tipo', 'sistema')
    expect(screen.getByText(/Carlos Ruiz · Proveedor/)).toBeInTheDocument()
  })

  it('un mensaje que llega en vivo aparece sin recargar y se marca como leído', async () => {
    const user = userEvent.setup()
    renderChat()
    await abrir(user)
    await screen.findByText(/Todavía no hay mensajes/)
    servicio.marcarLeido.mockClear()

    act(() => {
      conexion.manejadores?.onEstado('en-vivo')
      conexion.manejadores?.onMensaje(mensaje('9', 'Salimos en un rato'))
    })

    expect(await screen.findByText('Salimos en un rato')).toBeInTheDocument()
    expect(screen.queryByText(/Sin conexión en vivo/)).not.toBeInTheDocument()
    expect(servicio.marcarLeido).toHaveBeenCalledWith(ORDEN)
  })

  it('el mismo mensaje por el hub y por REST se ve una sola vez', async () => {
    const user = userEvent.setup()
    servicio.enviarMensaje.mockResolvedValue(mensaje('7', 'Confirmado', { autorId: 'u-yo', autorNombre: 'Marta Gómez', autorRol: 'Comprador' }))
    renderChat()
    await abrir(user)
    await screen.findByText(/Todavía no hay mensajes/)

    await user.type(screen.getByLabelText('Mensaje'), 'Confirmado')
    await user.click(screen.getByRole('button', { name: 'Enviar' }))
    act(() => conexion.manejadores?.onMensaje(mensaje('7', 'Confirmado', { autorId: 'u-yo' })))

    expect(await screen.findAllByText('Confirmado')).toHaveLength(1)
    expect(servicio.enviarMensaje).toHaveBeenCalledWith(ORDEN, 'Confirmado')
    expect(screen.getByLabelText('Mensaje')).toHaveValue('')
  })

  it('Enter envía el mensaje y Shift+Enter hace un salto de línea', async () => {
    const user = userEvent.setup()
    servicio.enviarMensaje.mockResolvedValue(mensaje('8', 'Hola\nmundo', { autorId: 'u-yo' }))
    renderChat()
    await abrir(user)
    await screen.findByText(/Todavía no hay mensajes/)

    await user.type(screen.getByLabelText('Mensaje'), 'Hola{Shift>}{Enter}{/Shift}mundo')
    expect(servicio.enviarMensaje).not.toHaveBeenCalled()
    expect(screen.getByLabelText('Mensaje')).toHaveValue('Hola\nmundo')

    await user.type(screen.getByLabelText('Mensaje'), '{Enter}')
    expect(servicio.enviarMensaje).toHaveBeenCalledWith(ORDEN, 'Hola\nmundo')
    expect(await screen.findByText(/Hola/)).toBeInTheDocument()
  })

  it('no deja enviar un mensaje vacío', async () => {
    const user = userEvent.setup()
    renderChat()
    await abrir(user)
    await screen.findByText(/Todavía no hay mensajes/)

    await user.click(screen.getByRole('button', { name: 'Enviar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Escribí un mensaje')
    expect(servicio.enviarMensaje).not.toHaveBeenCalled()
  })

  it('muestra el error del servidor al enviar', async () => {
    const user = userEvent.setup()
    servicio.enviarMensaje.mockRejectedValue(new Error('caído'))
    renderChat()
    await abrir(user)
    await screen.findByText(/Todavía no hay mensajes/)

    await user.type(screen.getByLabelText('Mensaje'), 'Hola')
    await user.click(screen.getByRole('button', { name: 'Enviar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo conectar con el servidor')
  })

  it('Compras ve "Marcar como resuelto" cuando hay problema, y al resolver avisa a la tarjeta', async () => {
    const user = userEvent.setup()
    servicio.resolverProblema.mockResolvedValue({ ordenCompraId: ORDEN, tieneProblemaAbierto: false })
    const { alCambiarProblema } = renderChat('Comprador', { problema: { abierto: true, motivo: 'Llegó incompleta' } })
    await abrir(user)

    await user.click(await screen.findByRole('button', { name: 'Marcar como resuelto' }))

    expect(servicio.resolverProblema).toHaveBeenCalledWith(ORDEN)
    expect(alCambiarProblema).toHaveBeenLastCalledWith({ abierto: false, motivo: null })
    expect(screen.queryByRole('button', { name: 'Marcar como resuelto' })).not.toBeInTheDocument()
  })

  it('el Proveedor nunca ve "Marcar como resuelto"', async () => {
    const user = userEvent.setup()
    renderChat('Proveedor', { problema: { abierto: true, motivo: 'Llegó incompleta' } })
    await abrir(user)
    await screen.findByText(/Todavía no hay mensajes/)

    expect(screen.queryByRole('button', { name: 'Marcar como resuelto' })).not.toBeInTheDocument()
  })

  it('un cambio de problema en vivo actualiza la tarjeta y el botón', async () => {
    const user = userEvent.setup()
    const { alCambiarProblema } = renderChat('Admin', { problema: { abierto: false, motivo: null } })
    await abrir(user)
    await screen.findByText(/Todavía no hay mensajes/)

    act(() =>
      conexion.manejadores?.onProblema({ ordenCompraId: ORDEN, tieneProblemaAbierto: true, motivoProblemaAbierto: 'Faltó cemento' }),
    )

    expect(await screen.findByRole('button', { name: 'Marcar como resuelto' })).toBeInTheDocument()
    expect(alCambiarProblema).toHaveBeenLastCalledWith({ abierto: true, motivo: 'Faltó cemento' })
  })

  it('si falla cargar el historial ofrece reintentar', async () => {
    const user = userEvent.setup()
    servicio.listarMensajes.mockRejectedValueOnce(new Error('caído')).mockResolvedValue([mensaje('1', 'Ya anda')])
    renderChat()
    await abrir(user)

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo conectar')
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByText('Ya anda')).toBeInTheDocument()
  })

  it('cierra la conexión en vivo al cerrar el chat', async () => {
    const user = userEvent.setup()
    renderChat()
    await abrir(user)
    await screen.findByText(/Todavía no hay mensajes/)

    await user.click(screen.getByRole('button', { name: /Cerrar el chat/ }))

    expect(conexion.cerrar).toHaveBeenCalled()
  })

  describe('sin conexión en vivo', () => {
    it('muestra el aviso discreto y consulta cada 10 segundos hasta que vuelve el tiempo real', async () => {
      const user = userEvent.setup()
      renderChat()
      await abrir(user)
      await screen.findByText(/Todavía no hay mensajes/)

      vi.useFakeTimers({ shouldAdvanceTime: true })
      act(() => conexion.manejadores?.onEstado('sin-conexion'))
      expect(screen.getByText(/Sin conexión en vivo/)).toBeInTheDocument()

      servicio.listarMensajes.mockClear()
      servicio.listarMensajes.mockResolvedValue([mensaje('5', 'Llegó por consulta')])
      await act(() => vi.advanceTimersByTimeAsync(9_000))
      expect(servicio.listarMensajes).not.toHaveBeenCalled()
      await act(() => vi.advanceTimersByTimeAsync(1_500))
      expect(servicio.listarMensajes).toHaveBeenCalledTimes(1)
      expect(await screen.findByText('Llegó por consulta')).toBeInTheDocument()

      // Vuelve el tiempo real: desaparece el aviso y deja de consultar cada 10 segundos.
      act(() => conexion.manejadores?.onEstado('en-vivo'))
      expect(screen.queryByText(/Sin conexión en vivo/)).not.toBeInTheDocument()
      servicio.listarMensajes.mockClear()
      await act(() => vi.advanceTimersByTimeAsync(35_000))
      expect(servicio.listarMensajes).not.toHaveBeenCalled()
    })

    it('en el respaldo también se entera de un problema abierto', async () => {
      const user = userEvent.setup()
      const { alCambiarProblema } = renderChat('Comprador')
      await abrir(user)
      await screen.findByText(/Todavía no hay mensajes/)

      vi.useFakeTimers({ shouldAdvanceTime: true })
      act(() => conexion.manejadores?.onEstado('sin-conexion'))
      servicio.obtenerProblemaDeOrden.mockResolvedValue({ abierto: true, motivo: 'Faltaron bolsas' })
      await act(() => vi.advanceTimersByTimeAsync(31_000))

      expect(alCambiarProblema).toHaveBeenLastCalledWith({ abierto: true, motivo: 'Faltaron bolsas' })
    })
  })
})
