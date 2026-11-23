import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ComponentProps } from 'react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'
import { server } from '../mocks/server'
import { CalificacionProveedor } from './CalificacionProveedor'

const ORDEN_ID = 'c1c2c3c4-0000-0000-0000-000000000001'

function renderar(props: Partial<ComponentProps<typeof CalificacionProveedor>> = {}) {
  const alGuardar = vi.fn()
  render(
    <CalificacionProveedor
      ordenCompraId={ORDEN_ID}
      nombreProveedor="Pedro Proveedor"
      titulo="¿Cómo cumplió el proveedor de la orden ABC?"
      recibidaCompleta
      calificacionActual={null}
      alGuardar={alGuardar}
      {...props}
    />,
  )
  return alGuardar
}

describe('CalificacionProveedor por orden (#222)', () => {
  it('muestra un mensaje claro si la API rechaza la calificación con 403 ProblemDetails', async () => {
    const user = userEvent.setup()
    server.use(http.put('*/api/ordenes-compra/:id/calificacion', () => HttpResponse.json(
      { title: 'Forbidden', status: 403, detail: 'User role not authorized' },
      { status: 403, headers: { 'Content-Type': 'application/problem+json' } },
    )))
    renderar()
    await user.click(screen.getByRole('button', { name: '5 de 5' }))
    await user.click(screen.getByRole('button', { name: 'Guardar calificación' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No tenés permiso para calificar esta orden.')
    expect(screen.getByRole('alert')).not.toHaveTextContent('User role not authorized')
  })

  it('bloquea la calificación antes de recibir todo el pedido', async () => {
    window.localStorage.setItem(`obrasync.calificacion.orden.${ORDEN_ID}`, '4')
    const alGuardar = renderar({ recibidaCompleta: false })

    expect(screen.getByText('Vas a poder calificar cuando llegue todo el pedido.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /de 5/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Guardar calificación' })).not.toBeInTheDocument()
    expect(alGuardar).not.toHaveBeenCalled()
    expect(window.localStorage.getItem(`obrasync.calificacion.orden.${ORDEN_ID}`)).toBeNull()
  })

  it('envía las estrellas y el comentario opcional al endpoint de la orden', async () => {
    const user = userEvent.setup()
    let url = ''
    let cuerpo: unknown
    server.use(http.put('*/api/ordenes-compra/:id/calificacion', async ({ params, request }) => {
      url = String(params.id)
      cuerpo = await request.json()
      return HttpResponse.json({
        ordenCompraId: ORDEN_ID,
        proveedorId: 'a1a2a3a4-0000-0000-0000-000000000001',
        usuarioId: 'b1b2b3b4-0000-0000-0000-000000000001',
        calificacion: 4,
        comentario: 'Llegó completo',
        fechaActualizacion: '2026-11-20T12:00:00Z',
        promedioProveedor: 4,
        cantidadCalificacionesProveedor: 1,
      })
    }))
    const alGuardar = renderar()

    await user.click(screen.getByRole('button', { name: '4 de 5' }))
    await user.type(screen.getByRole('textbox', { name: 'Comentario opcional, máximo 300 caracteres' }), 'Llegó completo')
    await user.click(screen.getByRole('button', { name: 'Guardar calificación' }))

    expect(await screen.findByText('Calificaste con 4 de 5.')).toBeInTheDocument()
    expect(url).toBe(ORDEN_ID)
    expect(cuerpo).toEqual({ calificacion: 4, comentario: 'Llegó completo' })
    expect(alGuardar).toHaveBeenCalledWith(4, 'Llegó completo')
  })

  it('limita el comentario a 300 caracteres', async () => {
    renderar()
    const campo = screen.getByRole('textbox', { name: 'Comentario opcional, máximo 300 caracteres' })
    fireEvent.change(campo, { target: { value: 'x'.repeat(301) } })

    expect(campo).toHaveValue('x'.repeat(300))
    expect(screen.getByText('300/300')).toBeInTheDocument()
  })

  it('permite guardar una calificación sin comentario', async () => {
    const user = userEvent.setup()
    let cuerpo: unknown
    server.use(http.put('*/api/ordenes-compra/:id/calificacion', async ({ request }) => {
      cuerpo = await request.json()
      return HttpResponse.json({
        ordenCompraId: ORDEN_ID,
        proveedorId: 'a1a2a3a4-0000-0000-0000-000000000001',
        usuarioId: 'b1b2b3b4-0000-0000-0000-000000000001',
        calificacion: 3,
        comentario: null,
        fechaActualizacion: '2026-11-21T12:00:00Z',
        promedioProveedor: 3,
        cantidadCalificacionesProveedor: 1,
      })
    }))
    renderar()
    await user.click(screen.getByRole('button', { name: '3 de 5' }))
    await user.click(screen.getByRole('button', { name: 'Guardar calificación' }))

    expect(await screen.findByText('Calificaste con 3 de 5.')).toBeInTheDocument()
    expect(cuerpo).toEqual({ calificacion: 3, comentario: null })
  })

  it('recupera del contrato el valor y permite cambiar la calificación existente', async () => {
    const user = userEvent.setup()
    let cuerpo: unknown
    server.use(http.put('*/api/ordenes-compra/:id/calificacion', async ({ request }) => {
      cuerpo = await request.json()
      return HttpResponse.json({
        ordenCompraId: ORDEN_ID,
        proveedorId: 'a1a2a3a4-0000-0000-0000-000000000001',
        usuarioId: 'b1b2b3b4-0000-0000-0000-000000000001',
        calificacion: 2,
        comentario: null,
        fechaActualizacion: '2026-11-21T12:00:00Z',
        promedioProveedor: 2,
        cantidadCalificacionesProveedor: 1,
      })
    }))
    renderar({ calificacionActual: 5, comentarioActual: 'Muy bien' })

    expect(screen.getByRole('status')).toHaveTextContent('Calificaste con 5 de 5.')
    expect(screen.getByText('Comentario: Muy bien')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Cambiar calificación' }))
    await user.click(screen.getByRole('button', { name: '2 de 5' }))
    await user.click(screen.getByRole('button', { name: 'Guardar calificación' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Calificaste con 2 de 5.')
    expect(cuerpo).toEqual({ calificacion: 2, comentario: 'Muy bien' })
  })

  it('usa GET por orden cuando el listado no incluyó la calificación del usuario', async () => {
    server.use(http.get('*/api/ordenes-compra/:id/calificacion', ({ params }) => {
      expect(params.id).toBe(ORDEN_ID)
      return HttpResponse.json({
        ordenCompraId: ORDEN_ID,
        proveedorId: 'a1a2a3a4-0000-0000-0000-000000000001',
        usuarioId: 'b1b2b3b4-0000-0000-0000-000000000001',
        calificacion: 4,
        comentario: 'Todo en orden',
        fechaActualizacion: '2026-11-21T12:00:00Z',
        promedioProveedor: 4,
        cantidadCalificacionesProveedor: 1,
      })
    }))
    renderar({ calificacionActual: undefined })

    expect(await screen.findByText('Calificaste con 4 de 5.')).toBeInTheDocument()
    expect(screen.getByText('Comentario: Todo en orden')).toBeInTheDocument()
  })

  it('explica el conflicto si la recepción dejó de estar completa antes de guardar', async () => {
    const user = userEvent.setup()
    server.use(http.put('*/api/ordenes-compra/:id/calificacion', () =>
      HttpResponse.json({ title: 'Conflicto', status: 409 }, { status: 409 }),
    ))
    renderar()
    await user.click(screen.getByRole('button', { name: '3 de 5' }))
    await user.click(screen.getByRole('button', { name: 'Guardar calificación' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Vas a poder calificar cuando llegue todo el pedido.')
  })
})
