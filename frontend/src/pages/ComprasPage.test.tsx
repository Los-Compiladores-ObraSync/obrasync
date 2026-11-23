import { render, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { PendientesContext } from '../notificaciones/pendientesContext'
import { server } from '../mocks/server'
import { ComprasPage } from './ComprasPage'

describe('ComprasPage', () => {
  it('contiene el enlace a Licitaciones y cotizaciones apuntando a /compras/licitaciones', () => {
    render(
      <MemoryRouter>
        <ComprasPage />
      </MemoryRouter>,
    )

    const link = screen.getByRole('link', { name: 'Licitaciones y cotizaciones' })
    expect(link).toBeInTheDocument()
    expect(link).toHaveAttribute('href', '/compras/licitaciones')
  })

  it('contiene el enlace a Presupuesto apuntando a /compras/presupuesto', () => {
    render(
      <MemoryRouter>
        <ComprasPage />
      </MemoryRouter>,
    )

    const link = screen.getByRole('link', { name: 'Presupuesto' })
    expect(link).toBeInTheDocument()
    expect(link).toHaveAttribute('href', '/compras/presupuesto')
  })

  it('contiene el enlace a Órdenes de compra apuntando a /compras/ordenes', () => {
    render(
      <MemoryRouter>
        <ComprasPage />
      </MemoryRouter>,
    )

    expect(screen.getByRole('link', { name: 'Órdenes de compra' })).toHaveAttribute(
      'href',
      '/compras/ordenes',
    )
  })

  it('contiene el enlace a Cargar insumos (CSV) apuntando a /compras/insumos/carga', () => {
    render(
      <MemoryRouter>
        <ComprasPage />
      </MemoryRouter>,
    )

    expect(screen.getByRole('link', { name: 'Cargar insumos (CSV)' })).toHaveAttribute(
      'href',
      '/compras/insumos/carga',
    )
  })

  it('tiene una salida al inicio (la pantalla no queda sin forma de volver)', () => {
    render(
      <MemoryRouter>
        <ComprasPage />
      </MemoryRouter>,
    )

    expect(screen.getByRole('link', { name: 'Volver al inicio' })).toHaveAttribute('href', '/')
  })

  it('ordena los accesos principales y mantiene visibles las Órdenes de compra', () => {
    render(
      <MemoryRouter>
        <ComprasPage />
      </MemoryRouter>,
    )

    const links = screen.getAllByRole('link')
    const textos = links.map((link) => link.textContent?.trim())
    expect(textos.indexOf('Aprobar requisiciones')).toBeLessThan(textos.indexOf('Historial de requisiciones'))
    expect(textos.indexOf('Historial de requisiciones')).toBeLessThan(textos.indexOf('Licitaciones y cotizaciones'))
    expect(textos.indexOf('Licitaciones y cotizaciones')).toBeLessThan(textos.indexOf('Órdenes de compra'))
    expect(textos.indexOf('Órdenes de compra')).toBeLessThan(textos.indexOf('Presupuesto'))
  })

  it('centraliza por licitar en el flujo y no repite indicadores en los accesos', async () => {
    server.use(http.get('*/api/licitaciones', () => HttpResponse.json([], { status: 200 })))
    render(<MemoryRouter><ComprasPage /></MemoryRouter>)
    expect(await screen.findByRole('region', { name: 'Progreso del flujo de compras' })).toBeInTheDocument()
    expect(screen.queryByRole('img', { name: /por licitar|su licitación/i })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Historial de requisiciones' })).toHaveAttribute('href', '/compras/requisiciones/historial')
  })

  it('muestra el estado vacío cuando las fuentes reales no tienen tareas', async () => {
    server.use(
      http.get('*/api/requisiciones', () => HttpResponse.json([])),
      http.get('*/api/licitaciones', () => HttpResponse.json([])),
      http.get('*/api/ordenes-compra', () => HttpResponse.json([])),
      http.get('*/api/recepciones/ordenes-pendientes', () => HttpResponse.json([])),
    )
    render(<PendientesContext.Provider value={{ cantidad: 0, pendientes: [], cargado: true, huboError: false, refrescar: () => {} }}><MemoryRouter><ComprasPage /></MemoryRouter></PendientesContext.Provider>)
    expect(await screen.findByText('No hay tareas pendientes. El flujo está al día.')).toBeInTheDocument()
  })

  it('muestra cantidades reales y prioriza las tareas según el flujo', async () => {
    server.use(
      http.get('*/api/requisiciones', () => HttpResponse.json([{ requisicionId: 'r2' }])),
      http.get('*/api/licitaciones', () => HttpResponse.json([
        { licitacionId: 'l1', requisicionId: 'r1', estado: 'Abierta' },
      ])),
      http.get('*/api/cotizaciones', ({ request }) => new URL(request.url).searchParams.get('licitacionId') === 'l1'
        ? HttpResponse.json([{ cotizacionId: 'c1' }])
        : HttpResponse.json([])),
      http.get('*/api/ordenes-compra', () => HttpResponse.json([
        { id: 'o1', fueDespachada: false, recibidaCompleta: false },
        { id: 'o2', fueDespachada: true, recibidaCompleta: false },
        { id: 'o3', fueDespachada: true, recibidaCompleta: true },
      ])),
      http.get('*/api/recepciones/ordenes-pendientes', () => HttpResponse.json([
        { ordenCompraId: 'o2', fechaDespacho: '2026-11-20T12:00:00Z' },
      ])),
    )
    render(<PendientesContext.Provider value={{ cantidad: 3, pendientes: [], cargado: true, huboError: false, refrescar: () => {} }}><MemoryRouter><ComprasPage /></MemoryRouter></PendientesContext.Provider>)

    await screen.findByText('1 por licitar · 1 por adjudicar')
    const etapas = within(screen.getByRole('region', { name: 'Progreso del flujo de compras' })).getAllByRole('link')
    expect(etapas.map((link) => link.textContent)).toEqual([
      'Requisición 3 por aprobar',
      'Licitación 1 por licitar · 1 por adjudicar',
      'Orden de compra 2 pendientes de seguimiento',
      'Recepción 1 orden pendiente de recibir',
    ])
    expect(etapas.map((link) => link.getAttribute('href'))).toEqual([
      '/compras/requisiciones', '/compras/licitaciones', '/compras/ordenes', '/compras/recepcion',
    ])
    const tareas = screen.getByRole('region', { name: 'Qué hago ahora' })
    await waitFor(() => {
      const enlaces = within(tareas).getAllByRole('link')
      expect(enlaces.map((link) => link.textContent)).toEqual([
        '3 requisiciones por aprobar →',
        '1 requisición aprobada pendiente de licitación →',
        '1 licitación con cotizaciones para adjudicar →',
        '2 órdenes pendientes de seguimiento →',
      ])
    })
  })

  it('muestra los diez movimientos más recientes en orden cronológico y enlaza con Ver todo', async () => {
    const requisiciones = Array.from({ length: 12 }, (_, indice) => ({
      requisicionId: `r-${indice}`,
      numeroRequisicion: `REQ-${indice}`,
      obraId: 'obra-1',
      estado: 'Aprobada',
      fechaCreacion: '2026-10-01T12:00:00Z',
      fechaAprobacion: `2026-11-${String(indice + 1).padStart(2, '0')}T12:00:00Z`,
    }))
    server.use(
      http.get('*/api/requisiciones', () => HttpResponse.json(requisiciones)),
      http.get('*/api/licitaciones', () => HttpResponse.json([
        { licitacionId: 'lic-adj', requisicionId: 'req-adj', numeroRequisicion: 'REQ-ADJ', obraId: 'obra-1', estado: 'Adjudicada', fechaCreacion: '2026-10-01T12:00:00Z', fechaLimiteCotizacion: '2026-10-15T12:00:00Z', fechaAdjudicacion: '2026-11-24T12:00:00Z', items: [] },
      ])),
      http.get('*/api/ordenes-compra', () => HttpResponse.json([])),
      http.get('*/api/recepciones', () => HttpResponse.json([])),
      http.get('*/api/recepciones/ordenes-pendientes', () => HttpResponse.json([])),
      http.get('*/api/obras', () => HttpResponse.json([])),
    )
    render(<MemoryRouter><ComprasPage /></MemoryRouter>)
    const region = await screen.findByRole('region', { name: 'Lo último que pasó' })
    const filas = await within(region).findAllByRole('listitem')
    expect(filas).toHaveLength(10)
    expect(filas[0]).toHaveTextContent('Licitación adjudicada')
    expect(filas[0]).toHaveTextContent('REQ-ADJ')
    expect(filas[1]).toHaveTextContent('REQ-11')
    expect(filas[9]).toHaveTextContent('REQ-3')
    expect(within(region).getByRole('link', { name: 'Ver todo' })).toHaveAttribute('href', '/compras/historial')
  })
})
