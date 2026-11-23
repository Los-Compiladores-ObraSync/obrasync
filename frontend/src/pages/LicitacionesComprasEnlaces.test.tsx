import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthContext } from '../auth/sessionContext'
import { reiniciarLicitacionesMock } from '../mocks/handlers'
import { LicitacionesComprasPage } from './LicitacionesComprasPage'

// Es la licitación abierta del mock (REQ-2026-0011).
const LIC_ABIERTA_ID = 'b1b2c3d4-1111-0000-0000-000000000001'

function renderComo(rol: string) {
  const sesion = {
    usuario: { id: 'u-1', nombreCompleto: 'Usuario de prueba', email: 'prueba@obrasync.com', rol },
    login: vi.fn(),
    logout: vi.fn(),
  }
  return render(
    <AuthContext.Provider value={sesion}>
      <MemoryRouter initialEntries={[`/compras/licitaciones?licitacionId=${LIC_ABIERTA_ID}`]}>
        <Routes>
          <Route path="/compras/licitaciones" element={<LicitacionesComprasPage />} />
        </Routes>
      </MemoryRouter>
    </AuthContext.Provider>,
  )
}

describe('LicitacionesComprasPage: enlaces para proveedores (#252)', () => {
  beforeEach(() => reiniciarLicitacionesMock())

  it('el Comprador ve los enlaces para proveedores en una licitación abierta', async () => {
    renderComo('Comprador')

    expect(await screen.findByRole('region', { name: 'Enlaces para proveedores' })).toBeInTheDocument()
  })

  it('el Admin no ve los enlaces: es de solo lectura', async () => {
    renderComo('Admin')

    // Las acciones de la licitación abierta ya están en pantalla, así que el resto también cargó.
    await screen.findByRole('region', { name: 'Acciones sobre la licitación abierta' })

    expect(screen.queryByRole('region', { name: 'Enlaces para proveedores' })).not.toBeInTheDocument()
  })
})
