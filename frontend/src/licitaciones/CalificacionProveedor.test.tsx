import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { server } from '../mocks/server'
import { CalificacionProveedor } from './CalificacionProveedor'

const PROVEEDOR_ID = 'f7a8b9c0-1111-2222-3333-444455556666'

function renderar(alGuardar = vi.fn()) {
  render(
    <CalificacionProveedor
      proveedorId={PROVEEDOR_ID}
      nombreProveedor="Pedro Proveedor"
      titulo="¿Cómo cumplió el proveedor de la orden ABC?"
      alGuardar={alGuardar}
    />,
  )
  return alGuardar
}

describe('CalificacionProveedor (#156)', () => {
  afterEach(() => {
    server.events.removeAllListeners()
  })

  it('Guardar calificación está apagado hasta que se elige una estrella', async () => {
    const user = userEvent.setup()
    renderar()

    expect(screen.getByRole('button', { name: 'Guardar calificación' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: '4 de 5' }))
    expect(screen.getByRole('button', { name: '4 de 5' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Guardar calificación' })).toBeEnabled()
  })

  it('guarda la calificación elegida, avisa y vuelve a apagar Guardar', async () => {
    const user = userEvent.setup()
    let cuerpo: unknown = null
    server.events.on('request:start', async ({ request }) => {
      if (request.method === 'PUT') cuerpo = await request.clone().json()
    })
    const alGuardar = renderar()

    await user.click(screen.getByRole('button', { name: '5 de 5' }))
    await user.click(screen.getByRole('button', { name: 'Guardar calificación' }))

    await waitFor(() => expect(cuerpo).toEqual({ calificacion: 5 }))
    expect(await screen.findByRole('status')).toHaveTextContent('Calificación guardada.')
    expect(alGuardar).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button', { name: 'Guardar calificación' })).toBeDisabled()
  })

  it('muestra un mensaje claro si no tenés permiso (403)', async () => {
    const user = userEvent.setup()
    server.use(
      http.put('*/api/cotizaciones/proveedores/:proveedorId/calificacion', () =>
        HttpResponse.json({ title: 'Prohibido', status: 403 }, { status: 403 }),
      ),
    )
    renderar()

    await user.click(screen.getByRole('button', { name: '3 de 5' }))
    await user.click(screen.getByRole('button', { name: 'Guardar calificación' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'No tenés permiso para calificar proveedores.',
    )
  })
})
