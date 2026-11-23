import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it } from 'vitest'
import { server } from '../mocks/server'
import { HistorialComprasPage } from './HistorialComprasPage'

const obraId = 'obra-1'
const idOrden = 'orden-00000001'
const requisiciones = [
  ...Array.from({ length: 21 }, (_, i) => ({
    requisicionId: `req-${i}`,
    numeroRequisicion: `REQ-${i}`,
    obraId,
    estado: 'Aprobada',
    fechaCreacion: '2026-10-01T12:00:00Z',
    fechaAprobacion: `2026-11-${String(i + 1).padStart(2, '0')}T12:00:00Z`,
  })),
  { requisicionId: 'req-rechazada', numeroRequisicion: 'REQ-R', obraId, estado: 'Rechazada', fechaCreacion: '2026-10-01T12:00:00Z', fechaRechazo: '2026-11-22T12:00:00Z' },
]

function Ubicacion() {
  const location = useLocation()
  return <output data-testid="ubicacion">{location.pathname + location.search}</output>
}

function configurarFuentes() {
  server.use(
    http.get('*/api/requisiciones', () => HttpResponse.json(requisiciones)),
    http.get('*/api/licitaciones', () => HttpResponse.json([
      { licitacionId: 'lic-pub', requisicionId: 'req-0', numeroRequisicion: 'REQ-0', obraId, estado: 'Abierta', fechaCreacion: '2026-11-23T12:00:00Z', fechaLimiteCotizacion: '2026-12-01T12:00:00Z', items: [] },
      { licitacionId: 'lic-adj', requisicionId: 'req-1', obraId, estado: 'Adjudicada', fechaCreacion: '2026-11-03T12:00:00Z', fechaLimiteCotizacion: '2026-11-10T12:00:00Z', items: [] },
      { licitacionId: 'lic-adj-2', requisicionId: 'req-2', obraId, estado: 'Adjudicada', fechaCreacion: '2026-11-04T12:00:00Z', fechaLimiteCotizacion: '2026-11-11T12:00:00Z', items: [] },
    ])),
    http.get('*/api/ordenes-compra', () => HttpResponse.json([
      { id: idOrden, licitacionId: 'lic-pub', cotizacionId: 'c1', obraId, proveedorId: 'p1', estado: 'Despachada', fechaEmision: '2026-11-21T12:00:00Z', fueDespachada: true, fechaDespacho: '2026-11-24T12:00:00Z' },
    ])),
    http.get('*/api/recepciones', ({ request }) => {
      if (new URL(request.url).searchParams.get('ordenCompraId') !== idOrden) return HttpResponse.json([])
      return HttpResponse.json([
        { id: 'rec-completa', ordenCompraId: idOrden, obraId, numeroRemito: 'REM-1', fechaRecepcion: '2026-11-25T12:00:00Z', createdAt: '2026-11-25T12:01:00Z', estado: 3 },
        { id: 'rec-problema', ordenCompraId: idOrden, obraId, numeroRemito: 'REM-P', fechaRecepcion: '2026-11-26T12:00:00Z', createdAt: '2026-11-26T12:01:00Z', estado: 4, motivoProblema: 'Llegó dañada' },
        { id: 'rec-problema', ordenCompraId: idOrden, obraId, numeroRemito: 'REM-P', fechaRecepcion: '2026-11-26T12:00:00Z', createdAt: '2026-11-26T12:01:00Z', estado: 4, motivoProblema: 'Llegó dañada' },
      ])
    }),
    http.get('*/api/obras', () => HttpResponse.json([{ id: obraId, nombre: 'Torre Alvear', ubicacion: 'CABA', fechaInicio: '2026-01-01', etapas: [] }])),
  )
}

function renderPagina(ruta = '/compras/historial') {
  return render(<MemoryRouter initialEntries={[ruta]}><HistorialComprasPage /><Ubicacion /></MemoryRouter>)
}

describe('HistorialComprasPage (#221)', () => {
  it('combina movimientos fechados, ordena descendente, deduplica y pagina el conjunto', async () => {
    configurarFuentes()
    renderPagina()
    const lista = await screen.findByRole('list')
    const filas = within(lista).getAllByRole('listitem')
    expect(filas).toHaveLength(20)
    expect(filas[0]).toHaveTextContent('Entrega con problema')
    expect(filas[1]).toHaveTextContent('Recepción completa')
    expect(filas[2]).toHaveTextContent('Orden de compra despachada')
    expect(within(lista).getByText('Licitación publicada')).toBeInTheDocument()
    expect(within(lista).getByText('Orden de compra emitida')).toBeInTheDocument()
    expect(within(lista).getByText('Requisición rechazada')).toBeInTheDocument()
    expect(within(lista).queryByText('Licitación adjudicada')).not.toBeInTheDocument()
    expect(within(lista).getAllByText('Entrega con problema')).toHaveLength(1)
    expect(screen.getByText(/Se detectaron 2 licitación\(es\) adjudicada\(s\)/)).toHaveTextContent('no abarcar todo el histórico')

    await userEvent.click(screen.getByRole('button', { name: 'Siguiente' }))
    expect(screen.getByTestId('ubicacion')).toHaveTextContent('pagina=2')
    expect(await screen.findByText('REQ-0')).toBeInTheDocument()
    const detalleRequisicion = screen.getByRole('link', { name: /Ver detalle: Requisición aprobada, REQ-0/ })
    expect(detalleRequisicion.getAttribute('href')).toContain('/compras/historial/requisiciones/req-0?')
    expect(detalleRequisicion.getAttribute('href')).toContain('pagina=2')
  })

  it('mantiene filtros en URL, filtra por tipo, obra y rango de fechas', async () => {
    configurarFuentes()
    renderPagina('/compras/historial?tipo=recepciones&obraId=obra-1&desde=2026-11-26&hasta=2026-11-26')
    const lista = await screen.findByRole('list')
    expect(within(lista).getAllByRole('listitem')).toHaveLength(1)
    expect(within(lista).getByText('Entrega con problema')).toBeInTheDocument()
    expect(screen.getByTestId('ubicacion')).toHaveTextContent('tipo=recepciones')
    expect(screen.queryByText(/Se detectaron .* licitación/)).not.toBeInTheDocument()
    const hrefDetalle = screen.getByRole('link', { name: /Ver detalle: Entrega con problema/ }).getAttribute('href') ?? ''
    expect(hrefDetalle).toContain(`/compras/ordenes?ordenCompraId=${idOrden}`)
    expect(hrefDetalle).toContain('tipo=recepciones')
    expect(hrefDetalle).toContain('desde=2026-11-26')

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Tipo de movimiento' }), 'requisiciones')
    expect(screen.getByTestId('ubicacion')).toHaveTextContent('tipo=requisiciones')
    expect(screen.getByTestId('ubicacion')).not.toHaveTextContent('pagina=')
  })

  it('muestra el conteo de adjudicaciones solo al ver todos los tipos o licitaciones', async () => {
    configurarFuentes()
    renderPagina()
    expect(await screen.findByText(/Se detectaron 2 licitación\(es\) adjudicada\(s\)/)).toBeInTheDocument()
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Tipo de movimiento' }), 'requisiciones')
    expect(screen.queryByText(/Se detectaron .* licitación/)).not.toBeInTheDocument()
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Tipo de movimiento' }), 'licitaciones')
    expect(screen.getByText(/Se detectaron 2 licitación\(es\) adjudicada\(s\)/)).toBeInTheDocument()
  })

  it('filtra por el día que se presenta en Buenos Aires aunque el ISO UTC cruce medianoche', async () => {
    configurarFuentes()
    server.use(http.get('*/api/recepciones', ({ request }) => {
      if (new URL(request.url).searchParams.get('ordenCompraId') !== idOrden) return HttpResponse.json([])
      return HttpResponse.json([{ id: 'rec-zona', ordenCompraId: idOrden, obraId, numeroRemito: 'REM-Z', fechaRecepcion: '2026-11-27T02:00:00Z', createdAt: '2026-11-27T02:00:00Z', estado: 3 }])
    }))
    renderPagina('/compras/historial?tipo=recepciones&desde=2026-11-26&hasta=2026-11-26')
    const lista = await screen.findByRole('list')
    expect(within(lista).getAllByRole('listitem')).toHaveLength(1)
    expect(within(lista).getByText('26 de noviembre de 2026')).toBeInTheDocument()
  })

  it('quita de la URL los límites de fecha que no son días reales', async () => {
    configurarFuentes()
    renderPagina('/compras/historial?desde=2026-02-31&hasta=2026-11-26')
    await screen.findByRole('list')
    expect(screen.getByLabelText('Fecha desde')).toHaveValue('')
    expect(screen.getByTestId('ubicacion')).not.toHaveTextContent('desde=')
    expect(screen.getByTestId('ubicacion')).toHaveTextContent('hasta=2026-11-26')
  })

  it('conserva la página y los filtros al recargar la vista', async () => {
    configurarFuentes()
    renderPagina('/compras/historial?tipo=requisiciones&obraId=obra-1&pagina=2')
    expect(await screen.findByText('REQ-0')).toBeInTheDocument()
    expect(screen.getByTestId('ubicacion')).toHaveTextContent('tipo=requisiciones&obraId=obra-1&pagina=2')
  })

  it('no reescribe una página uno válida que ya estaba en la URL', async () => {
    configurarFuentes()
    renderPagina('/compras/historial?tipo=requisiciones&pagina=1')
    expect(await screen.findByText('Página 1 de 2 · 22 movimientos')).toBeInTheDocument()
    expect(screen.getByTestId('ubicacion')).toHaveTextContent('tipo=requisiciones&pagina=1')
  })

  it('normaliza una página superior a la última y elimina páginas cero o negativas', async () => {
    configurarFuentes()
    const { unmount } = renderPagina('/compras/historial?tipo=requisiciones&pagina=99')
    expect(await screen.findByText('Página 2 de 2 · 22 movimientos')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByTestId('ubicacion')).toHaveTextContent('pagina=2'))
    unmount()

    for (const pagina of ['0', '-3']) {
      const vista = renderPagina(`/compras/historial?tipo=requisiciones&pagina=${pagina}`)
      expect(await screen.findByText('Página 1 de 2 · 22 movimientos')).toBeInTheDocument()
      await waitFor(() => expect(screen.getByTestId('ubicacion')).not.toHaveTextContent('pagina='))
      vista.unmount()
    }
  })

  it('muestra las fuentes disponibles cuando falla un servicio', async () => {
    configurarFuentes()
    server.use(http.get('*/api/licitaciones', () => new HttpResponse(null, { status: 500 })))
    renderPagina()
    expect(await screen.findByText(/No se pudieron cargar algunos datos/)).toBeInTheDocument()
    expect(await screen.findByText('Entrega con problema')).toBeInTheDocument()
    expect(screen.queryByText('Licitación publicada')).not.toBeInTheDocument()
  })

  it('muestra un estado vacío después de aplicar filtros sin resultados', async () => {
    configurarFuentes()
    renderPagina('/compras/historial?tipo=recepciones&desde=2027-01-01')
    expect(await screen.findByText('No hay movimientos para mostrar.')).toBeInTheDocument()
  })
})
