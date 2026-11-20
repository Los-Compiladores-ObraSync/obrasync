import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import { AuthContext, type SesionUsuario } from '../auth/sessionContext'
import { server } from '../mocks/server'
import { claveVistos, leerVistos } from './novedades'
import { NovedadesProvider } from './NovedadesProvider'
import { useNovedades } from './useNovedades'

function Avisos() {
  const { cantidad, novedades, marcarVistas } = useNovedades()
  return (
    <>
      <p data-testid="cantidad">{cantidad}</p>
      <p data-testid="ids">{novedades.map((n) => n.id).join(',')}</p>
      <button type="button" onClick={() => marcarVistas()}>
        Marcar todas
      </button>
      <button type="button" onClick={() => marcarVistas([novedades[0]])}>
        Marcar la primera
      </button>
    </>
  )
}

function renderizar(rol: string, intervaloMs?: number) {
  const usuario: SesionUsuario = {
    id: 'u-1',
    nombreCompleto: 'Persona de Prueba',
    email: 'prueba@obrasync.com',
    rol,
  }
  return render(
    <AuthContext.Provider value={{ usuario, login: () => {}, logout: () => {} }}>
      <NovedadesProvider intervaloMs={intervaloMs}>
        <Avisos />
      </NovedadesProvider>
    </AuthContext.Provider>,
  )
}

function req(id: string, estado: 'Pendiente' | 'Aprobada' | 'Rechazada') {
  return {
    requisicionId: id,
    numeroRequisicion: `REQ-${id}`,
    obraId: 'o-1',
    solicitanteId: 'u-1',
    estado,
    fechaCreacion: '2026-11-01T10:00:00Z',
    items: [],
  }
}

function lic(id: string, yaCotizada = false) {
  return {
    licitacionId: id,
    requisicionId: 'r-1',
    numeroRequisicion: `REQ-${id}`,
    obraId: 'o-1',
    estado: 'Abierta',
    fechaCreacion: '2026-11-01T10:00:00Z',
    fechaLimiteCotizacion: '2026-11-30T10:00:00Z',
    items: [],
    yaCotizada,
  }
}

function simularRequisiciones(lista: () => unknown[]) {
  const pedidos: number[] = []
  server.use(
    http.get('*/api/requisiciones', () => {
      pedidos.push(1)
      return HttpResponse.json(lista())
    }),
  )
  return pedidos
}

describe('NovedadesProvider (#163)', () => {
  beforeEach(() => localStorage.clear())

  it('Capataz, primera vez: lo ya resuelto no avisa', async () => {
    const pedidos = simularRequisiciones(() => [req('a', 'Aprobada'), req('b', 'Rechazada')])
    renderizar('Capataz')

    await waitFor(() => expect(pedidos.length).toBeGreaterThan(0))
    await waitFor(() => expect(leerVistos(claveVistos('capataz', 'u-1'))).not.toBeNull())
    expect(screen.getByTestId('cantidad')).toHaveTextContent('0')
  })

  it('Capataz: avisa cuando una requisición suya cambia de estado, y no repite lo visto', async () => {
    const user = userEvent.setup()
    let lista = [req('a', 'Pendiente')]
    simularRequisiciones(() => lista)
    renderizar('Capataz', 50)

    await waitFor(() => expect(leerVistos(claveVistos('capataz', 'u-1'))).not.toBeNull())
    expect(screen.getByTestId('cantidad')).toHaveTextContent('0')

    lista = [req('a', 'Aprobada')]
    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('1'))
    expect(screen.getByTestId('ids')).toHaveTextContent('a')

    await user.click(screen.getByRole('button', { name: 'Marcar todas' }))
    expect(screen.getByTestId('cantidad')).toHaveTextContent('0')
    expect(leerVistos(claveVistos('capataz', 'u-1'))).toEqual({ a: 'Aprobada' })

    // Aunque se vuelva a consultar, ya no avisa.
    await new Promise((r) => setTimeout(r, 200))
    expect(screen.getByTestId('cantidad')).toHaveTextContent('0')
  })

  it('Proveedor: avisa de las licitaciones abiertas sin cotizar y se puede marcar una sola', async () => {
    const user = userEvent.setup()
    server.use(
      http.get('*/api/licitaciones', () =>
        HttpResponse.json([lic('x'), lic('y', true), lic('z')]),
      ),
    )
    renderizar('Proveedor')

    await waitFor(() => expect(screen.getByTestId('cantidad')).toHaveTextContent('2'))
    const primera = screen.getByTestId('ids').textContent?.split(',')[0]

    await user.click(screen.getByRole('button', { name: 'Marcar la primera' }))

    expect(screen.getByTestId('cantidad')).toHaveTextContent('1')
    expect(leerVistos(claveVistos('proveedor', 'u-1'))).toEqual({ [primera as string]: 'visto' })
  })

  it('con error de red no rompe y no inventa avisos', async () => {
    server.use(http.get('*/api/licitaciones', () => HttpResponse.error()))
    renderizar('Proveedor')

    await new Promise((r) => setTimeout(r, 100))
    expect(screen.getByTestId('cantidad')).toHaveTextContent('0')
  })

  it('Comprador y Admin no consultan nada (tienen sus propios avisos)', async () => {
    let pedidos = 0
    server.use(
      http.get('*/api/requisiciones', () => {
        pedidos++
        return HttpResponse.json([])
      }),
      http.get('*/api/licitaciones', () => {
        pedidos++
        return HttpResponse.json([])
      }),
    )
    renderizar('Comprador')

    await new Promise((r) => setTimeout(r, 100))
    expect(pedidos).toBe(0)
    expect(screen.getByTestId('cantidad')).toHaveTextContent('0')
  })
})
