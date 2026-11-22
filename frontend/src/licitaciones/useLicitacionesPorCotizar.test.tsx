import { renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { useLicitacionesPorCotizar } from './useLicitacionesPorCotizar'

describe('useLicitacionesPorCotizar', () => {
  it('cuenta solo las que el proveedor todavía no cotizó', async () => {
    server.use(
      http.get('*/api/licitaciones', () =>
        HttpResponse.json([
          { licitacionId: 'l-1', yaCotizada: false },
          { licitacionId: 'l-2', yaCotizada: true },
          { licitacionId: 'l-3' },
        ]),
      ),
    )
    const { result } = renderHook(() => useLicitacionesPorCotizar(true))

    await waitFor(() => expect(result.current.cantidad).toBe(2))
    expect(result.current.huboError).toBe(false)
  })

  it('si falla el pedido, marca el error', async () => {
    server.use(http.get('*/api/licitaciones', () => new HttpResponse(null, { status: 500 })))
    const { result } = renderHook(() => useLicitacionesPorCotizar(true))

    await waitFor(() => expect(result.current.huboError).toBe(true))
    expect(result.current.cantidad).toBeNull()
  })

  it('inactivo: no pide nada', async () => {
    let pedidos = 0
    server.use(
      http.get('*/api/licitaciones', () => {
        pedidos += 1
        return HttpResponse.json([])
      }),
    )
    const { result } = renderHook(() => useLicitacionesPorCotizar(false))

    await new Promise((r) => setTimeout(r, 50))
    expect(pedidos).toBe(0)
    expect(result.current.cantidad).toBeNull()
  })
})
