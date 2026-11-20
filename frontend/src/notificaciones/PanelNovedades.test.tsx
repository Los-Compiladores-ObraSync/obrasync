import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import type { Novedad } from './novedades'
import { PanelNotificaciones } from './PanelNotificaciones'

const aprobada: Novedad = {
  id: 'a',
  marca: 'Aprobada',
  tipo: 'requisicion-aprobada',
  numero: 'REQ-0001',
  ruta: '/obra/requisiciones/a',
}
const rechazada: Novedad = {
  id: 'b',
  marca: 'Rechazada',
  tipo: 'requisicion-rechazada',
  numero: 'REQ-0002',
  ruta: '/obra/requisiciones/b',
}
const licitacion: Novedad = {
  id: 'l',
  marca: 'visto',
  tipo: 'licitacion-nueva',
  numero: 'REQ-0003',
  ruta: '/proveedores/licitaciones/l',
}

function renderizar(novedades: Novedad[], onMarcar = vi.fn(), onNavegar = vi.fn()) {
  render(
    <MemoryRouter>
      <PanelNotificaciones
        id="panel"
        novedades={novedades}
        onMarcarNovedades={onMarcar}
        onNavegar={onNavegar}
      />
    </MemoryRouter>,
  )
  return { onMarcar, onNavegar }
}

describe('PanelNotificaciones: novedades del Capataz y del Proveedor (#163)', () => {
  it('muestra las requisiciones aprobadas y rechazadas del Capataz', () => {
    renderizar([aprobada, rechazada])

    expect(screen.getByText('Tenés 2 novedades en tus requisiciones')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Tu requisición REQ-0001 fue aprobada' })).toHaveAttribute(
      'href',
      '/obra/requisiciones/a',
    )
    expect(screen.getByRole('link', { name: 'Tu requisición REQ-0002 fue rechazada' })).toBeInTheDocument()
    expect(screen.queryByText('No tenés avisos')).not.toBeInTheDocument()
  })

  it('muestra las licitaciones nuevas del Proveedor, en singular si es una', () => {
    renderizar([licitacion])

    expect(screen.getByText('Hay 1 licitación nueva para cotizar')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Licitación abierta: REQ-0003' })).toHaveAttribute(
      'href',
      '/proveedores/licitaciones/l',
    )
  })

  it('al tocar un aviso lo marca como visto y cierra el panel', async () => {
    const user = userEvent.setup()
    const { onMarcar, onNavegar } = renderizar([aprobada, rechazada])

    await user.click(screen.getByRole('link', { name: 'Tu requisición REQ-0001 fue aprobada' }))

    expect(onMarcar).toHaveBeenCalledWith([aprobada])
    expect(onNavegar).toHaveBeenCalled()
  })

  it('"Marcar como vistas" marca todas', async () => {
    const user = userEvent.setup()
    const { onMarcar } = renderizar([aprobada, rechazada])

    await user.click(screen.getByRole('button', { name: 'Marcar como vistas' }))

    expect(onMarcar).toHaveBeenCalledWith()
  })

  it('lista pocas y avisa cuántas más hay', () => {
    const muchas = ['1', '2', '3', '4', '5'].map((n) => ({ ...aprobada, id: n, numero: `REQ-${n}` }))
    renderizar(muchas)

    expect(screen.getAllByRole('link')).toHaveLength(3)
    expect(screen.getByText('y 2 más')).toBeInTheDocument()
  })

  it('sin novedades ni otros avisos sigue diciendo que no hay avisos', () => {
    renderizar([])
    expect(screen.getByText('No tenés avisos')).toBeInTheDocument()
  })
})
