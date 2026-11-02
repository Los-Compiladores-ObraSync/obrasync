import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthContext, type SesionUsuario } from '../auth/sessionContext'
import { reiniciarRequisicionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { MAX_PENDIENTES_EN_TABLERO } from '../notificaciones/configuracionAvisos'
import { PendientesContext, type PendientesContextValue } from '../notificaciones/pendientesContext'
import { PendientesProvider } from '../notificaciones/PendientesProvider'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import { PendientesPage } from './PendientesPage'

const OBRA_ID = 'b1b2c3d4-0000-0000-0000-0000000000a1'

const comprador: SesionUsuario = {
  id: 'a1b2c3d4-0000-0000-0000-000000000002',
  nombreCompleto: 'Marta Gómez',
  email: 'comprador@obrasync.com',
  rol: 'Comprador',
}

function requisicion(n: number, fechaCreacion: string): RequisicionResponseDto {
  const numero = String(n).padStart(4, '0')
  return {
    requisicionId: `d1d2d3d4-0000-0000-0000-00000000${numero}`,
    numeroRequisicion: `REQ-2026-${numero}`,
    obraId: OBRA_ID,
    etapa: 'Estructura',
    solicitanteId: 'a1b2c3d4-0000-0000-0000-000000000001',
    aprobadorId: null,
    estado: 'Pendiente',
    observaciones: null,
    fechaCreacion,
    fechaAprobacion: null,
    items: [],
  }
}

/** La tarjeta "Requisiciones por aprobar" (el título y el número van juntos). */
function tarjeta() {
  return screen.getByRole('region', { name: 'Requisiciones por aprobar' })
}

/** Renderiza la página con un valor del contexto a mano (sin pedir nada al servidor). */
function renderConContexto(valor: Partial<PendientesContextValue>) {
  const completo: PendientesContextValue = {
    cantidad: valor.pendientes?.length ?? 0,
    pendientes: [],
    cargado: true,
    huboError: false,
    refrescar: () => {},
    ...valor,
  }
  return render(
    <MemoryRouter>
      <PendientesContext.Provider value={completo}>
        <PendientesPage />
      </PendientesContext.Provider>
    </MemoryRouter>,
  )
}

describe('PendientesPage', () => {
  beforeEach(() => {
    reiniciarRequisicionesMock()
  })

  it('mientras no llegó la primera respuesta muestra "Cargando…"', () => {
    renderConContexto({ cargado: false })

    expect(screen.getByRole('status')).toHaveTextContent('Cargando…')
  })

  it('muestra la cantidad y las requisiciones con obra, etapa y fecha', async () => {
    renderConContexto({ pendientes: [requisicion(11, '2026-10-19T12:00:00Z')] })

    expect(screen.getByRole('heading', { level: 2, name: 'Requisiciones por aprobar' })).toBeInTheDocument()
    expect(tarjeta()).toHaveTextContent('Requisiciones por aprobar1')
    const lista = screen.getByRole('list', { name: 'Requisiciones más antiguas' })
    expect(within(lista).getByText('REQ-2026-0011')).toBeInTheDocument()
    expect(within(lista).getByText(/Etapa: Estructura/)).toBeInTheDocument()
    expect(within(lista).getByText('Creada el 19 de octubre de 2026')).toBeInTheDocument()
    // El nombre de la obra llega de GET /api/obras (el mock la llama Torre Alvear).
    expect(await within(lista).findByText(/Obra: Torre Alvear/)).toBeInTheDocument()
  })

  it('las más antiguas van primero', () => {
    renderConContexto({
      pendientes: [
        requisicion(3, '2026-10-30T12:00:00Z'),
        requisicion(1, '2026-10-01T12:00:00Z'),
        requisicion(2, '2026-10-15T12:00:00Z'),
      ],
    })

    const numeros = within(screen.getByRole('list', { name: 'Requisiciones más antiguas' }))
      .getAllByRole('listitem')
      .map((li) => li.textContent?.slice(0, 13))
    expect(numeros).toEqual(['REQ-2026-0001', 'REQ-2026-0002', 'REQ-2026-0003'])
  })

  it('ordena por fecha real aunque unas lleguen con fracción de segundo y otras no', () => {
    renderConContexto({
      pendientes: [
        requisicion(1, '2026-10-19T12:00:00.500Z'), // medio segundo DESPUÉS de la 0002
        requisicion(2, '2026-10-19T12:00:00Z'),
      ],
    })

    const numeros = within(screen.getByRole('list', { name: 'Requisiciones más antiguas' }))
      .getAllByRole('listitem')
      .map((li) => li.textContent?.slice(0, 13))
    // Comparando como texto quedaba primero la 0001 ('.' va antes que 'Z'): estaba mal.
    expect(numeros).toEqual(['REQ-2026-0002', 'REQ-2026-0001'])
  })

  it('muestra solo las más antiguas y avisa cuántas hay en total', () => {
    const total = MAX_PENDIENTES_EN_TABLERO + 3
    const pendientes = Array.from({ length: total }, (_, i) =>
      requisicion(i + 1, `2026-10-${String(i + 1).padStart(2, '0')}T12:00:00Z`),
    )
    renderConContexto({ pendientes })

    const lista = screen.getByRole('list', { name: 'Requisiciones más antiguas' })
    expect(within(lista).getAllByRole('listitem')).toHaveLength(MAX_PENDIENTES_EN_TABLERO)
    expect(
      screen.getByText(`Mostrando las ${MAX_PENDIENTES_EN_TABLERO} más antiguas de ${total}.`),
    ).toBeInTheDocument()
  })

  it('con pocas requisiciones no muestra el aviso de "más antiguas"', () => {
    renderConContexto({ pendientes: [requisicion(1, '2026-10-01T12:00:00Z')] })

    expect(screen.queryByText(/Mostrando las/)).not.toBeInTheDocument()
  })

  it('tiene un enlace a Aprobar requisiciones', () => {
    renderConContexto({ pendientes: [requisicion(1, '2026-10-01T12:00:00Z')] })

    expect(screen.getByRole('link', { name: 'Ir a Aprobar requisiciones' })).toHaveAttribute(
      'href',
      '/compras/requisiciones',
    )
  })

  it('sin pendientes dice "Estás al día" y no ofrece aprobar', () => {
    renderConContexto({ pendientes: [] })

    expect(screen.getByText('Estás al día')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Ir a Aprobar requisiciones' })).not.toBeInTheDocument()
  })

  it('si no hay datos y falló el pedido, ofrece Reintentar', async () => {
    const refrescar = vi.fn()
    const user = userEvent.setup()
    renderConContexto({ cargado: false, huboError: true, refrescar })

    expect(screen.getByRole('alert')).toHaveTextContent(
      'No se pudieron cargar los pendientes. Intentá nuevamente.',
    )
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(refrescar).toHaveBeenCalledTimes(1)
  })

  it('si falla un refresco pero ya había datos, sigue mostrándolos', () => {
    renderConContexto({
      pendientes: [requisicion(1, '2026-10-01T12:00:00Z')],
      huboError: true,
    })

    expect(screen.getByText('REQ-2026-0001')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('si falla la lista de obras muestra el identificador de la obra', async () => {
    server.use(
      http.get('*/api/obras', () => HttpResponse.json({ status: 500 }, { status: 500 })),
    )
    renderConContexto({ pendientes: [requisicion(1, '2026-10-01T12:00:00Z')] })

    expect(await screen.findByText(new RegExp(`Obra: ${OBRA_ID}`))).toBeInTheDocument()
  })

  it('con el proveedor real: usa la lista del mock (2 pendientes) sin pedirla dos veces', async () => {
    let pedidos = 0
    server.use(
      http.get('*/api/requisiciones', ({ request }) => {
        pedidos += 1
        const estado = new URL(request.url).searchParams.get('estado')
        return HttpResponse.json(estado === 'Pendiente' ? [requisicion(11, '2026-10-19T12:00:00Z'), requisicion(12, '2026-10-19T12:30:00Z')] : [])
      }),
    )
    render(
      <AuthContext.Provider value={{ usuario: comprador, login: () => {}, logout: () => {} }}>
        <MemoryRouter>
          <PendientesProvider>
            <PendientesPage />
          </PendientesProvider>
        </MemoryRouter>
      </AuthContext.Provider>,
    )

    expect(await screen.findByText('REQ-2026-0011')).toBeInTheDocument()
    expect(tarjeta()).toHaveTextContent('Requisiciones por aprobar2')
    expect(pedidos).toBe(1)
  })
})
