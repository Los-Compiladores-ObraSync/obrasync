import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { AuthContext, type SesionUsuario } from '../auth/sessionContext'
import { reiniciarLicitacionesMock, reiniciarRequisicionesMock } from '../mocks/handlers'
import { PendientesProvider } from '../notificaciones/PendientesProvider'
import { InicioPage } from './InicioPage'

function renderInicio(rol: string | null) {
  const usuario: SesionUsuario | null =
    rol === null
      ? null
      : { id: 'u-1', nombreCompleto: 'Ana Pérez', email: 'ana@obrasync.com', rol }
  return render(
    <AuthContext.Provider value={{ usuario, login: () => {}, logout: () => {} }}>
      <PendientesProvider>
        <MemoryRouter>
          <InicioPage />
        </MemoryRouter>
      </PendientesProvider>
    </AuthContext.Provider>,
  )
}

describe('InicioPage', () => {
  beforeEach(() => {
    reiniciarRequisicionesMock()
    reiniciarLicitacionesMock()
  })

  it('saluda con el nombre y muestra rol, email e iniciales del usuario', () => {
    renderInicio('Capataz')

    expect(screen.getByRole('heading', { level: 1, name: 'Bienvenido, Ana Pérez' })).toBeInTheDocument()
    const datos = screen.getByRole('region', { name: 'Tus datos' })
    expect(datos).toHaveTextContent('Capataz')
    expect(datos).toHaveTextContent('ana@obrasync.com')
    expect(datos).toHaveTextContent('AP')
  })

  it('Capataz: tarjeta del asistente destacada y sus botones, sin resumen de aprobaciones', () => {
    renderInicio('Capataz')

    expect(screen.getByRole('link', { name: 'Asistente' })).toHaveAttribute('href', '/obra/asistente')
    expect(screen.getByRole('link', { name: 'Nueva requisición' })).toHaveAttribute(
      'href',
      '/obra/requisicion/nueva',
    )
    expect(screen.getByRole('link', { name: 'Recibir mercadería' })).toHaveAttribute(
      'href',
      '/obra/recepcion',
    )
    expect(screen.getByRole('link', { name: 'Historial de requisiciones' })).toHaveAttribute(
      'href',
      '/obra/requisiciones',
    )
    expect(screen.getByRole('link', { name: 'Presupuesto' })).toHaveAttribute('href', '/obra/presupuesto')
    expect(screen.queryByRole('region', { name: 'Resumen de aprobaciones' })).not.toBeInTheDocument()
    expect(screen.queryByText('Licitaciones abiertas')).not.toBeInTheDocument()
  })

  it('Comprador: ve el resumen de aprobaciones y el asistente de su portal', async () => {
    renderInicio('Comprador')

    expect(await screen.findByRole('region', { name: 'Resumen de aprobaciones' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Asistente' })).toHaveAttribute('href', '/compras/asistente')
    expect(screen.getByRole('link', { name: 'Ver obras' })).toHaveAttribute('href', '/compras/obras')
    expect(screen.getByRole('link', { name: 'Presupuesto' })).toHaveAttribute('href', '/compras/presupuesto')
    // Los botones pasan a ser secundarios cuando hay tarjeta de aprobaciones.
    expect(screen.getByRole('link', { name: 'Ver obras' })).toHaveClass('accion-secundaria')
    expect(screen.queryByRole('link', { name: 'Nueva requisición' })).not.toBeInTheDocument()
  })

  it('Comprador: el Historial muestra el mismo número de pendientes que la campana', async () => {
    renderInicio('Comprador')

    const historial = screen.getByRole('link', { name: /Historial de requisiciones/ })
    expect(await within(historial).findByRole('img', { name: /para aprobar/ })).toBeInTheDocument()
  })

  it('Capataz: no ve el número de pendientes en ningún botón', async () => {
    renderInicio('Capataz')

    expect(screen.queryByRole('img', { name: /para aprobar/ })).not.toBeInTheDocument()
  })

  it('Admin: ve el resumen de aprobaciones y las acciones de obra y de compras', async () => {
    renderInicio('Admin')

    expect(await screen.findByRole('region', { name: 'Resumen de aprobaciones' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Nueva requisición' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Nueva obra' })).toHaveAttribute('href', '/compras/obras/nueva')
    expect(screen.getByRole('link', { name: 'Presupuesto' })).toHaveAttribute('href', '/compras/presupuesto')
  })

  it('Proveedor: ve las licitaciones abiertas y desde el Inicio llega al Historial de cotizaciones y a Mis órdenes', async () => {
    renderInicio('Proveedor')

    expect(screen.getByRole('heading', { level: 2, name: 'Licitaciones abiertas' })).toBeInTheDocument()
    expect((await screen.findAllByRole('link', { name: /Cotizar/ })).length).toBeGreaterThan(0)
    expect(screen.queryByRole('region', { name: 'Resumen de aprobaciones' })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Historial de cotizaciones' })).toHaveAttribute(
      'href',
      '/proveedores/mis-cotizaciones',
    )
    expect(screen.getByRole('link', { name: 'Mis órdenes' })).toHaveAttribute('href', '/proveedores/mis-ordenes')
  })

  it('un rol sin portales muestra el aviso para consultar con un administrador', () => {
    renderInicio('Invitado')

    expect(screen.getByRole('status')).toHaveTextContent('Tu rol todavía no tiene portales asignados')
  })

  it('sin sesión no se rompe: no muestra acciones ni aviso de rol', () => {
    renderInicio(null)

    expect(screen.getByRole('heading', { level: 1 })).toBeInTheDocument()
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })
})
