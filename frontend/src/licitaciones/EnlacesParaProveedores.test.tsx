import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reiniciarLicitacionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { EnlacesParaProveedores } from './EnlacesParaProveedores'
import { urlPublicaMagicLink } from './magicLinkRutas'
import { textoVigencia } from './magicLinkTextos'

// Es la licitación abierta del mock (REQ-2026-0011).
const LICITACION_ID = 'b1b2c3d4-1111-0000-0000-000000000001'

const DOS_PROVEEDORES = [
  { id: 'p-1', nombreCompleto: 'Ana Corralón', telefono: '+54 9 11 5555-1111' },
  { id: 'p-2', nombreCompleto: 'Beto Materiales', telefono: '+54 9 11 5555-2222' },
]

type Usuario = ReturnType<typeof userEvent.setup>

async function abrir(user: Usuario) {
  await user.click(screen.getByRole('button', { name: 'Generar enlaces para proveedores' }))
  await screen.findAllByRole('checkbox')
}

async function abrirYElegir(user: Usuario, ...nombres: string[]) {
  await abrir(user)
  for (const nombre of nombres) await user.click(screen.getByRole('checkbox', { name: nombre }))
}

function conProveedores(proveedores: unknown[]) {
  server.use(http.get('*/api/usuarios/proveedores', () => HttpResponse.json(proveedores)))
}

describe('EnlacesParaProveedores', () => {
  beforeEach(() => {
    reiniciarLicitacionesMock()
    // jsdom no abre ventanas: se simula que el navegador sí deja abrir WhatsApp.
    vi.spyOn(window, 'open').mockReturnValue({} as Window)
  })

  it('empieza cerrado y no pide los proveedores hasta que se abre', async () => {
    const user = userEvent.setup()
    let pedidos = 0
    server.use(
      http.get('*/api/usuarios/proveedores', () => {
        pedidos += 1
        return HttpResponse.json([])
      }),
    )
    render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
    expect(pedidos).toBe(0)

    await user.click(screen.getByRole('button', { name: 'Generar enlaces para proveedores' }))

    expect(await screen.findByText('No hay proveedores activos para invitar.')).toBeInTheDocument()
    expect(pedidos).toBe(1)
  })

  it('no deja generar hasta elegir al menos un proveedor', async () => {
    const user = userEvent.setup()
    render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

    await abrir(user)

    expect(screen.getByRole('button', { name: 'Generar enlace' })).toBeDisabled()
  })

  it('genera el enlace del proveedor elegido y lo muestra completo', async () => {
    const user = userEvent.setup()
    render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

    await abrirYElegir(user, 'Carlos Ruiz')
    await user.click(screen.getByRole('button', { name: 'Generar enlace' }))

    const campo = await screen.findByLabelText('Enlace para Carlos Ruiz')
    // Es la dirección de la pantalla pública, no la ruta de la API que devuelve el backend.
    expect(campo).toHaveValue(urlPublicaMagicLink('token-simulado-1'))
    expect(screen.getByText(/Sirve una sola vez y vence/)).toBeInTheDocument()
    expect(screen.getByText(/dura 1 hora/)).toBeInTheDocument()
  })

  it('con un solo proveedor abre WhatsApp con el mensaje listo y deja el botón por si no se abre', async () => {
    const user = userEvent.setup()
    conProveedores([{ id: 'p-wsp', nombreCompleto: 'Carlos Ruiz', telefono: '+54 9 11 5555-1234' }])
    render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

    await abrirYElegir(user, 'Carlos Ruiz')
    await user.click(screen.getByRole('button', { name: 'Generar enlace' }))
    await screen.findByLabelText('Enlace para Carlos Ruiz')

    // Directo al chat del proveedor (solo dígitos, con código de país) y con el enlace dentro del mensaje.
    const esperado = window.open as unknown as ReturnType<typeof vi.fn>
    expect(esperado).toHaveBeenCalledTimes(1)
    const url = String(esperado.mock.calls[0][0])
    expect(url.startsWith('https://wa.me/5491155551234?text=')).toBe(true)
    expect(decodeURIComponent(url)).toContain(urlPublicaMagicLink('token-simulado-1'))
    expect(screen.getByRole('link', { name: 'Enviar por WhatsApp a Carlos Ruiz' })).toHaveAttribute('href', url)
    expect(screen.queryByText(/WhatsApp te va a pedir que elijas el contacto/)).not.toBeInTheDocument()
  })

  it('si el proveedor no tiene teléfono con código de país, WhatsApp deja elegir el contacto', async () => {
    const user = userEvent.setup()
    conProveedores([{ id: 'p-sin', nombreCompleto: 'Carlos Ruiz', telefono: '11 5555-1234' }])
    render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

    await abrirYElegir(user, 'Carlos Ruiz')
    await user.click(screen.getByRole('button', { name: 'Generar enlace' }))
    await screen.findByLabelText('Enlace para Carlos Ruiz')

    expect(
      screen.getByRole('link', { name: 'Enviar por WhatsApp a Carlos Ruiz' }).getAttribute('href'),
    ).toMatch(/^https:\/\/wa\.me\/\?text=/)
    expect(screen.getByText(/WhatsApp te va a pedir que elijas el contacto/)).toBeInTheDocument()
  })

  it('si el navegador bloquea la ventana, avisa que se use el botón', async () => {
    vi.spyOn(window, 'open').mockReturnValue(null)
    const user = userEvent.setup()
    render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

    await abrirYElegir(user, 'Carlos Ruiz')
    await user.click(screen.getByRole('button', { name: 'Generar enlace' }))

    expect(await screen.findByText(/Tocá "Enviar por WhatsApp"/)).toBeInTheDocument()
  })

  it('manda al backend el proveedor elegido', async () => {
    const user = userEvent.setup()
    let cuerpo: unknown
    server.use(
      http.post('*/api/licitaciones/:id/magic-links', async ({ request }) => {
        cuerpo = await request.json()
        return HttpResponse.json({
          token: 'abc_-123',
          hash: 'abc_-123',
          url: '/api/licitaciones/magic-links/abc_-123/cotizaciones',
          fechaDeVencimiento: new Date(Date.now() + 3_600_000).toISOString(),
          expiresIn: '1 hour',
          horasVigencia: 1,
        })
      }),
    )
    render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

    await abrirYElegir(user, 'Carlos Ruiz')
    await user.click(screen.getByRole('button', { name: 'Generar enlace' }))

    expect(await screen.findByLabelText('Enlace para Carlos Ruiz')).toHaveValue(urlPublicaMagicLink('abc_-123'))
    expect(cuerpo).toEqual({ proveedorId: 'a1b2c3d4-0000-0000-0000-000000000003', proveedorNombre: 'Carlos Ruiz' })
  })

  it('copia el enlace y avisa', async () => {
    const user = userEvent.setup()
    render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

    await abrirYElegir(user, 'Carlos Ruiz')
    await user.click(screen.getByRole('button', { name: 'Generar enlace' }))
    await screen.findByLabelText('Enlace para Carlos Ruiz')
    await user.click(screen.getByRole('button', { name: 'Copiar enlace de Carlos Ruiz' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Copiamos el enlace de Carlos Ruiz')
    expect(await navigator.clipboard.readText()).toBe(urlPublicaMagicLink('token-simulado-1'))
  })

  it('si el navegador no deja copiar, avisa que el enlace quedó seleccionado', async () => {
    const user = userEvent.setup()
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('sin permiso'))
    render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

    await abrirYElegir(user, 'Carlos Ruiz')
    await user.click(screen.getByRole('button', { name: 'Generar enlace' }))
    await screen.findByLabelText('Enlace para Carlos Ruiz')
    await user.click(screen.getByRole('button', { name: 'Copiar enlace de Carlos Ruiz' }))

    expect(await screen.findByRole('status')).toHaveTextContent('No pudimos copiarlo solo')
  })

  describe('varios proveedores a la vez', () => {
    it('genera un enlace distinto para cada proveedor elegido', async () => {
      const user = userEvent.setup()
      conProveedores(DOS_PROVEEDORES)
      const cuerpos: unknown[] = []
      server.use(
        http.post('*/api/licitaciones/:id/magic-links', async ({ request }) => {
          cuerpos.push(await request.json())
          return HttpResponse.json({
            token: `tok-${cuerpos.length}`,
            hash: `tok-${cuerpos.length}`,
            url: `/api/licitaciones/magic-links/tok-${cuerpos.length}/cotizaciones`,
            fechaDeVencimiento: new Date(Date.now() + 3_600_000).toISOString(),
            expiresIn: '1 hour',
            horasVigencia: 1,
          })
        }),
      )
      render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

      await abrirYElegir(user, 'Ana Corralón', 'Beto Materiales')
      await user.click(screen.getByRole('button', { name: 'Generar 2 enlaces' }))

      expect(await screen.findByLabelText('Enlace para Ana Corralón')).toHaveValue(urlPublicaMagicLink('tok-1'))
      expect(screen.getByLabelText('Enlace para Beto Materiales')).toHaveValue(urlPublicaMagicLink('tok-2'))
      // Un pedido por proveedor, cada uno con su identificador: los enlaces no se comparten.
      expect(cuerpos).toEqual([
        { proveedorId: 'p-1', proveedorNombre: 'Ana Corralón' },
        { proveedorId: 'p-2', proveedorNombre: 'Beto Materiales' },
      ])
      expect(screen.getByRole('link', { name: 'Enviar por WhatsApp a Ana Corralón' })).toBeInTheDocument()
      expect(screen.getByRole('link', { name: 'Enviar por WhatsApp a Beto Materiales' })).toBeInTheDocument()
    })

    it('no abre WhatsApp solo (el navegador deja una ventana por toque) y explica cómo mandarlos', async () => {
      const user = userEvent.setup()
      conProveedores(DOS_PROVEEDORES)
      render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

      await abrirYElegir(user, 'Ana Corralón', 'Beto Materiales')
      await user.click(screen.getByRole('button', { name: 'Generar 2 enlaces' }))

      await screen.findByLabelText('Enlace para Beto Materiales')
      expect(window.open).not.toHaveBeenCalled()
      expect(screen.getByRole('status')).toHaveTextContent('Cada proveedor tiene su propio enlace')
    })

    it('"Elegir todos" marca a todos y después deja quitar la selección', async () => {
      const user = userEvent.setup()
      conProveedores(DOS_PROVEEDORES)
      render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)
      await abrir(user)

      await user.click(screen.getByRole('button', { name: 'Elegir todos' }))
      expect(screen.getByRole('checkbox', { name: 'Ana Corralón' })).toBeChecked()
      expect(screen.getByRole('checkbox', { name: 'Beto Materiales' })).toBeChecked()
      expect(screen.getByRole('button', { name: 'Generar 2 enlaces' })).toBeEnabled()

      await user.click(screen.getByRole('button', { name: 'Quitar la selección' }))
      expect(screen.getByRole('checkbox', { name: 'Ana Corralón' })).not.toBeChecked()
      expect(screen.getByRole('button', { name: 'Generar enlace' })).toBeDisabled()
    })

    it('si falla uno, muestra los enlaces de los demás y avisa cuál falló', async () => {
      const user = userEvent.setup()
      conProveedores(DOS_PROVEEDORES)
      server.use(
        http.post('*/api/licitaciones/:id/magic-links', async ({ request }) => {
          const { proveedorId } = (await request.json()) as { proveedorId: string }
          if (proveedorId === 'p-1') return HttpResponse.json({ title: 'Error' }, { status: 500 })
          return HttpResponse.json({
            token: 'tok-ok',
            hash: 'tok-ok',
            url: '/api/licitaciones/magic-links/tok-ok/cotizaciones',
            fechaDeVencimiento: new Date(Date.now() + 3_600_000).toISOString(),
            expiresIn: '1 hour',
            horasVigencia: 1,
          })
        }),
      )
      render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

      await abrirYElegir(user, 'Ana Corralón', 'Beto Materiales')
      await user.click(screen.getByRole('button', { name: 'Generar 2 enlaces' }))

      expect(await screen.findByLabelText('Enlace para Beto Materiales')).toHaveValue(urlPublicaMagicLink('tok-ok'))
      expect(screen.queryByLabelText('Enlace para Ana Corralón')).not.toBeInTheDocument()
      expect(screen.getByRole('alert')).toHaveTextContent('No pudimos generar el enlace de Ana Corralón')
    })

    it('si la licitación ya no está abierta, avisa una sola vez y no sigue pidiendo', async () => {
      const user = userEvent.setup()
      conProveedores(DOS_PROVEEDORES)
      let pedidos = 0
      server.use(
        http.post('*/api/licitaciones/:id/magic-links', () => {
          pedidos += 1
          return HttpResponse.json({ title: 'Licitación no disponible', status: 409 }, { status: 409 })
        }),
      )
      render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

      await abrirYElegir(user, 'Ana Corralón', 'Beto Materiales')
      await user.click(screen.getByRole('button', { name: 'Generar 2 enlaces' }))

      expect(await screen.findByRole('alert')).toHaveTextContent('ya no está abierta o venció el plazo')
      expect(screen.getAllByRole('alert')).toHaveLength(1)
      expect(pedidos).toBe(1)
    })
  })

  it('explica que la licitación ya no está abierta cuando el backend responde 409', async () => {
    const user = userEvent.setup()
    server.use(
      http.post('*/api/licitaciones/:id/magic-links', () =>
        HttpResponse.json({ title: 'Licitación no disponible para generar enlace', status: 409 }, { status: 409 }),
      ),
    )
    render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

    await abrirYElegir(user, 'Carlos Ruiz')
    await user.click(screen.getByRole('button', { name: 'Generar enlace' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('ya no está abierta o venció el plazo')
    expect(screen.queryByLabelText('Enlace para Carlos Ruiz')).not.toBeInTheDocument()
  })

  it('si falla la lista de proveedores deja reintentar', async () => {
    const user = userEvent.setup()
    server.use(
      http.get('*/api/usuarios/proveedores', () => HttpResponse.json({ title: 'Error' }, { status: 500 }), {
        once: true,
      }),
    )
    render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

    await user.click(screen.getByRole('button', { name: 'Generar enlaces para proveedores' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos cargar los proveedores')

    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByRole('checkbox', { name: 'Carlos Ruiz' })).toBeInTheDocument()
  })
})

describe('textoVigencia', () => {
  it('escribe la duración en español', () => {
    expect(textoVigencia(1)).toBe('1 hora')
    expect(textoVigencia(2)).toBe('2 horas')
    expect(textoVigencia(1.5)).toBe('1,5 horas')
    expect(textoVigencia(0.5)).toBe('30 minutos')
  })
})

describe('urlPublicaMagicLink', () => {
  it('arma la dirección pública con el origen, la ruta y el token', () => {
    expect(urlPublicaMagicLink('abc_-123', 'https://app.test')).toBe('https://app.test/cotizar/abc_-123')
  })
})
