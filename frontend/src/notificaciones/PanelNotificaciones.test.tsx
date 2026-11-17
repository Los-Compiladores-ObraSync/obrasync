import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import { PanelNotificaciones } from './PanelNotificaciones'

function crearRequisicion(id: string, numero: string): RequisicionResponseDto {
  return {
    requisicionId: id,
    obraId: 'obra-1',
    numeroRequisicion: numero,
    solicitanteId: 'user-1',
    estado: 'Pendiente',
    etapa: 'Estructura',
    fechaCreacion: '2026-10-01T10:00:00Z',
    items: [],
  }
}

function renderPanel({
  cantidadPendientes = 0,
  pendientes = [],
  cantidadBloqueadas = 0,
  onNavegar = () => {},
}: {
  cantidadPendientes?: number
  pendientes?: RequisicionResponseDto[]
  cantidadBloqueadas?: number
  onNavegar?: () => void
}) {
  return render(
    <MemoryRouter>
      <PanelNotificaciones
        id="panel-avisos"
        cantidadPendientes={cantidadPendientes}
        pendientes={pendientes}
        cantidadBloqueadas={cantidadBloqueadas}
        onNavegar={onNavegar}
      />
    </MemoryRouter>,
  )
}

describe('PanelNotificaciones', () => {
  it('muestra solo requisiciones cuando solo hay pendientes', () => {
    renderPanel({
      cantidadPendientes: 3,
      pendientes: [crearRequisicion('req-1', 'REQ-001')],
      cantidadBloqueadas: 0,
    })

    expect(screen.getByText('Tenés 3 requisiciones para aprobar')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ir a Aprobar requisiciones' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /REQ-001/ })).toBeInTheDocument()
    expect(screen.queryByText(/bloqueadas por presupuesto/i)).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Ir a Presupuesto' })).not.toBeInTheDocument()
  })

  it('muestra solo órdenes bloqueadas cuando no hay requisiciones', () => {
    renderPanel({
      cantidadPendientes: 0,
      pendientes: [],
      cantidadBloqueadas: 2,
    })

    expect(screen.getByText('2 órdenes de compra bloqueadas por presupuesto')).toBeInTheDocument()
    const linkPresupuesto = screen.getByRole('link', { name: 'Ir a Presupuesto' })
    expect(linkPresupuesto).toBeInTheDocument()
    expect(linkPresupuesto).toHaveAttribute('href', '/compras/presupuesto')
    expect(screen.queryByText(/requisiciones para aprobar/i)).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Ir a Aprobar requisiciones' })).not.toBeInTheDocument()
  })

  it('usa singular para una sola orden bloqueada', () => {
    renderPanel({
      cantidadPendientes: 0,
      cantidadBloqueadas: 1,
    })

    expect(screen.getByText('1 orden de compra bloqueada por presupuesto')).toBeInTheDocument()
  })

  it('muestra ambos avisos cuando existen ambos tipos de pendientes', () => {
    renderPanel({
      cantidadPendientes: 2,
      pendientes: [crearRequisicion('req-1', 'REQ-001')],
      cantidadBloqueadas: 4,
    })

    expect(screen.getByText('Tenés 2 requisiciones para aprobar')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ir a Aprobar requisiciones' })).toBeInTheDocument()
    expect(screen.getByText('4 órdenes de compra bloqueadas por presupuesto')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ir a Presupuesto' })).toBeInTheDocument()
  })

  it('no muestra aviso de órdenes cuando cantidadBloqueadas es cero', () => {
    renderPanel({
      cantidadPendientes: 1,
      pendientes: [crearRequisicion('req-1', 'REQ-001')],
      cantidadBloqueadas: 0,
    })

    expect(screen.queryByText(/bloqueadas por presupuesto/i)).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Ir a Presupuesto' })).not.toBeInTheDocument()
  })

  it('el enlace a presupuesto navega a /compras/presupuesto y cierra el panel mediante onNavegar', async () => {
    const onNavegar = vi.fn()
    const user = userEvent.setup()

    renderPanel({
      cantidadBloqueadas: 2,
      onNavegar,
    })

    const link = screen.getByRole('link', { name: 'Ir a Presupuesto' })
    expect(link).toHaveAttribute('href', '/compras/presupuesto')

    await user.click(link)
    expect(onNavegar).toHaveBeenCalledTimes(1)
  })

  it('sin avisos muestra "No tenés avisos" y ningún enlace', () => {
    renderPanel({
      cantidadPendientes: 0,
      cantidadBloqueadas: 0,
    })

    expect(screen.getByText('No tenés avisos')).toBeInTheDocument()
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })

  it('los enlaces actuales de requisiciones siguen funcionando y llaman a onNavegar', async () => {
    const onNavegar = vi.fn()
    const user = userEvent.setup()

    renderPanel({
      cantidadPendientes: 1,
      pendientes: [crearRequisicion('req-1', 'REQ-001')],
      onNavegar,
    })

    const linkReq = screen.getByRole('link', { name: /REQ-001/ })
    expect(linkReq).toHaveAttribute('href', '/compras/requisiciones?req=req-1')

    await user.click(linkReq)
    expect(onNavegar).toHaveBeenCalledTimes(1)

    const linkAprobar = screen.getByRole('link', { name: 'Ir a Aprobar requisiciones' })
    expect(linkAprobar).toHaveAttribute('href', '/compras/requisiciones')

    await user.click(linkAprobar)
    expect(onNavegar).toHaveBeenCalledTimes(2)
  })
})
