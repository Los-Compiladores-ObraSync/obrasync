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

async function abrirYElegir(user: ReturnType<typeof userEvent.setup>, nombre: string) {
  await user.click(screen.getByRole('button', { name: 'Generar enlace para un proveedor' }))
  await screen.findByRole('option', { name: nombre })
  await user.selectOptions(screen.getByLabelText('Proveedor'), nombre)
}

describe('EnlacesParaProveedores', () => {
  beforeEach(() => reiniciarLicitacionesMock())

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

    expect(screen.queryByLabelText('Proveedor')).not.toBeInTheDocument()
    expect(pedidos).toBe(0)

    await user.click(screen.getByRole('button', { name: 'Generar enlace para un proveedor' }))

    expect(await screen.findByText('No hay proveedores activos para invitar.')).toBeInTheDocument()
    expect(pedidos).toBe(1)
  })

  it('no deja generar hasta elegir un proveedor', async () => {
    const user = userEvent.setup()
    render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

    await user.click(screen.getByRole('button', { name: 'Generar enlace para un proveedor' }))
    await screen.findByRole('option', { name: 'Carlos Ruiz' })

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
    await user.click(screen.getByRole('button', { name: 'Copiar enlace' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Copiamos el enlace')
    expect(await navigator.clipboard.readText()).toBe(urlPublicaMagicLink('token-simulado-1'))
  })

  it('si el navegador no deja copiar, avisa que el enlace quedó seleccionado', async () => {
    const user = userEvent.setup()
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('sin permiso'))
    render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

    await abrirYElegir(user, 'Carlos Ruiz')
    await user.click(screen.getByRole('button', { name: 'Generar enlace' }))
    await screen.findByLabelText('Enlace para Carlos Ruiz')
    await user.click(screen.getByRole('button', { name: 'Copiar enlace' }))

    expect(await screen.findByRole('status')).toHaveTextContent('No pudimos copiarlo solo')
  })

  it('al cambiar de proveedor saca el enlace del anterior', async () => {
    const user = userEvent.setup()
    server.use(
      http.get('*/api/usuarios/proveedores', () =>
        HttpResponse.json([
          { id: 'p-1', nombreCompleto: 'Ana Corralón' },
          { id: 'p-2', nombreCompleto: 'Beto Materiales' },
        ]),
      ),
    )
    render(<EnlacesParaProveedores licitacionId={LICITACION_ID} />)

    await abrirYElegir(user, 'Ana Corralón')
    await user.click(screen.getByRole('button', { name: 'Generar enlace' }))
    await screen.findByLabelText('Enlace para Ana Corralón')

    await user.selectOptions(screen.getByLabelText('Proveedor'), 'Beto Materiales')

    expect(screen.queryByLabelText('Enlace para Ana Corralón')).not.toBeInTheDocument()
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

    await user.click(screen.getByRole('button', { name: 'Generar enlace para un proveedor' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos cargar los proveedores')

    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByRole('option', { name: 'Carlos Ruiz' })).toBeInTheDocument()
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
