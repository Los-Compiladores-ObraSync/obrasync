import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { reiniciarLicitacionesMock } from '../mocks/handlers'
import { server } from '../mocks/server'
import { LicitacionesComprasPage } from './LicitacionesComprasPage'

const LIC_ID = 'b1b2c3d4-1111-0000-0000-000000000001'
const OFERTAS = [
  { cotizacionId: 'oferta-a', proveedorId: 'proveedor-a', proveedorNombre: 'Alfa', puntajeTotal: 90, precio: 300, flete: 30, plazo: 7, calificacionProveedor: 4 },
  { cotizacionId: 'oferta-b', proveedorId: 'proveedor-b', proveedorNombre: 'Beta', puntajeTotal: 80, precio: 100, flete: 10, plazo: 3, calificacionProveedor: null },
  { cotizacionId: 'oferta-c', proveedorId: 'proveedor-c', proveedorNombre: 'Gamma', puntajeTotal: 70, precio: 200, flete: 20, plazo: 5, calificacionProveedor: 2 },
].map((oferta) => ({
  cotizacionId: oferta.cotizacionId,
  licitacionId: LIC_ID,
  proveedorId: oferta.proveedorId,
  proveedorNombre: oferta.proveedorNombre,
  puntajeTotal: oferta.puntajeTotal,
  flete: oferta.flete,
  plazoEntregaDias: oferta.plazo,
  calificacionProveedor: oferta.calificacionProveedor,
  fechaCreacion: '2026-10-22T10:00:00Z',
  items: [{ articuloId: 'c1c2c3c4-0000-0000-0000-000000000001', precioUnitario: oferta.precio }],
  esGanadora: false,
}))

function SearchProbe() {
  const location = useLocation()
  return <output aria-label="URL actual">{location.search}</output>
}

function renderPagina(search = `?licitacionId=${LIC_ID}`) {
  return render(
    <MemoryRouter initialEntries={[`/compras/licitaciones${search}`]}>
      <SearchProbe />
      <Routes>
        <Route path="/compras/licitaciones" element={<LicitacionesComprasPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('orden de ofertas en Compras', () => {
  beforeEach(() => {
    reiniciarLicitacionesMock()
    server.use(http.get('*/api/cotizaciones', () => HttpResponse.json(OFERTAS)))
  })

  async function esperarLista() {
    const lista = await screen.findByRole('list', { name: /^Cotizaciones recibidas para / })
    await within(lista).findByText('Alfa')
    return lista
  }

  it('reemplaza el selector de estrellas y muestra Mejor puntaje seleccionado por defecto', async () => {
    renderPagina()
    const lista = await esperarLista()
    expect(screen.queryByText('¿Qué buscás en una oferta?')).not.toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Ordenar ofertas' })).toHaveValue('mejor-puntaje')
    expect(Array.from(lista.querySelectorAll('li')).map((li) => li.textContent).join(' ')).toMatch(/Alfa.*Beta.*Gamma/)
  })

  it.each([
    ['precio-asc', /Beta.*Gamma.*Alfa/],
    ['precio-desc', /Alfa.*Gamma.*Beta/],
    ['flete-asc', /Beta.*Gamma.*Alfa/],
    ['flete-desc', /Alfa.*Gamma.*Beta/],
    ['plazo-asc', /Beta.*Gamma.*Alfa/],
    ['plazo-desc', /Alfa.*Gamma.*Beta/],
    ['calificacion-desc', /Alfa.*Gamma.*Beta/],
    ['calificacion-asc', /Gamma.*Alfa.*Beta/],
  ])('aplica el orden %s localmente', async (orden, esperado) => {
    let consultas = 0
    server.use(http.get('*/api/cotizaciones', () => {
      consultas += 1
      return HttpResponse.json(OFERTAS)
    }))
    const user = userEvent.setup()
    renderPagina()
    const lista = await esperarLista()
    await user.selectOptions(screen.getByRole('combobox', { name: 'Ordenar ofertas' }), orden)
    expect(Array.from(lista.querySelectorAll('li')).map((li) => li.textContent).join(' ')).toMatch(esperado)
    expect(consultas).toBe(1)
  })

  it('actualiza orden en la URL y conserva parámetros ajenos; al abrir la URL compartida recupera el criterio', async () => {
    const user = userEvent.setup()
    const { unmount } = renderPagina(`?licitacionId=${LIC_ID}&vista=compacta`)
    await esperarLista()
    await user.selectOptions(screen.getByRole('combobox', { name: 'Ordenar ofertas' }), 'flete-asc')
    const urlFinal = screen.getByLabelText('URL actual').textContent ?? ''
    const params = new URLSearchParams(urlFinal)
    expect(params.get('orden')).toBe('flete-asc')
    expect(params.get('vista')).toBe('compacta')

    unmount()
    renderPagina(urlFinal)
    await esperarLista()
    expect(screen.getAllByRole('combobox', { name: 'Ordenar ofertas' }).at(-1)).toHaveValue('flete-asc')
  })

  it('usa Mejor puntaje si el parámetro orden es inválido y conserva los demás parámetros', async () => {
    renderPagina(`?licitacionId=${LIC_ID}&vista=compacta&orden=barato`)
    await esperarLista()
    expect(screen.getByRole('combobox', { name: 'Ordenar ofertas' })).toHaveValue('mejor-puntaje')
  })
})
