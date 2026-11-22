import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { textoLicitacionesParaCotizar } from '../notificaciones/textos'
import { ResumenLicitaciones } from './ResumenLicitaciones'

function renderResumen(cantidad: number | null, huboError = false) {
  return render(
    <MemoryRouter>
      <ResumenLicitaciones cantidad={cantidad} huboError={huboError} />
    </MemoryRouter>,
  )
}

describe('textoLicitacionesParaCotizar', () => {
  it('usa singular con una y plural con varias', () => {
    expect(textoLicitacionesParaCotizar(1)).toBe('Tenés 1 licitación para cotizar')
    expect(textoLicitacionesParaCotizar(3)).toBe('Tenés 3 licitaciones para cotizar')
  })
})

describe('ResumenLicitaciones', () => {
  it('con licitaciones para cotizar muestra cuántas y el botón que lleva a cotizarlas', () => {
    renderResumen(2)

    expect(screen.getByText('Tenés 2 licitaciones para cotizar')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Cotizar licitaciones' })).toHaveAttribute(
      'href',
      '/proveedores/mis-cotizaciones?estado=pendientes',
    )
  })

  it('si no hay ninguna para cotizar, dice que está al día y no muestra el botón', () => {
    renderResumen(0)

    expect(screen.getByText('Estás al día')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Cotizar licitaciones' })).not.toBeInTheDocument()
  })

  it('mientras carga muestra "Cargando…"', () => {
    renderResumen(null)

    expect(screen.getByRole('status')).toHaveTextContent('Cargando…')
  })

  it('si falla la carga, avisa y deja el botón para entrar igual', () => {
    renderResumen(null, true)

    expect(screen.getByRole('alert')).toHaveTextContent('No se pudo saber cuántas licitaciones')
    expect(screen.getByRole('link', { name: 'Cotizar licitaciones' })).toBeInTheDocument()
  })
})
