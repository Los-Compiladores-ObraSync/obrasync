import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { CampanaConPendientes } from './CampanaConPendientes'
import { UMBRAL_PENDIENTES } from './configuracionAvisos'
import { PendientesContext } from './pendientesContext'

function renderizar(cantidad: number) {
    return render(
        <MemoryRouter>
        <PendientesContext.Provider value={{ cantidad, refrescar: () => { } }}>
            <p>Afuera </p>
            < CampanaConPendientes />
            </PendientesContext.Provider>
            </MemoryRouter>,
  )
}

function campana() {
    return screen.getByRole('button', { name: /Notificaciones|Atención/ })
}

describe('CampanaConPendientes - panel de avisos', () => {
    it('arranca cerrado', () => {
        renderizar(3)

        expect(screen.queryByRole('region', { name: 'Avisos' })).not.toBeInTheDocument()
        expect(campana()).toHaveAttribute('aria-expanded', 'false')
    })

    it('al tocar la campana muestra cuántas requisiciones hay para aprobar', async () => {
        const usuario = userEvent.setup()
        renderizar(3)

        await usuario.click(campana())

        expect(screen.getByText('Tenés 3 requisiciones para aprobar')).toBeInTheDocument()
        expect(campana()).toHaveAttribute('aria-expanded', 'true')
    })

    it('usa el singular cuando hay una sola', async () => {
        const usuario = userEvent.setup()
        renderizar(1)

        await usuario.click(campana())

        expect(screen.getByText('Tenés 1 requisición para aprobar')).toBeInTheDocument()
    })

    it('el enlace lleva a Aprobar requisiciones', async () => {
        const usuario = userEvent.setup()
        renderizar(2)

        await usuario.click(campana())

        expect(
            screen.getByRole('link', { name: 'Ir a Aprobar requisiciones' }),
        ).toHaveAttribute('href', '/compras/requisiciones')
    })

    it('sin pendientes muestra "No tenés avisos" y ningún enlace', async () => {
        const usuario = userEvent.setup()
        renderizar(0)

        await usuario.click(campana())

        expect(screen.getByText('No tenés avisos')).toBeInTheDocument()
        expect(screen.queryByRole('link')).not.toBeInTheDocument()
    })

    it('tocar la campana otra vez lo cierra', async () => {
        const usuario = userEvent.setup()
        renderizar(2)

        await usuario.click(campana())
        await usuario.click(campana())

        expect(screen.queryByRole('region', { name: 'Avisos' })).not.toBeInTheDocument()
    })

    it('Escape lo cierra y el foco vuelve a la campana', async () => {
        const usuario = userEvent.setup()
        renderizar(2)

        await usuario.click(campana())
        await usuario.tab() // el foco pasa al enlace del panel
        expect(screen.getByRole('link', { name: 'Ir a Aprobar requisiciones' })).toHaveFocus()

        await usuario.keyboard('{Escape}')

        expect(screen.queryByRole('region', { name: 'Avisos' })).not.toBeInTheDocument()
        expect(campana()).toHaveFocus()
    })

    it('tocar afuera lo cierra', async () => {
        const usuario = userEvent.setup()
        renderizar(2)

        await usuario.click(campana())
        await usuario.click(screen.getByText('Afuera'))

        expect(screen.queryByRole('region', { name: 'Avisos' })).not.toBeInTheDocument()
    })

    it('tocar adentro del panel no lo cierra', async () => {
        const usuario = userEvent.setup()
        renderizar(2)

        await usuario.click(campana())
        await usuario.click(screen.getByText('Tenés 2 requisiciones para aprobar'))

        expect(screen.getByRole('region', { name: 'Avisos' })).toBeInTheDocument()
    })

    it('al tocar el enlace el panel se cierra', async () => {
        const usuario = userEvent.setup()
        renderizar(2)

        await usuario.click(campana())
        await usuario.click(screen.getByRole('link', { name: 'Ir a Aprobar requisiciones' }))

        expect(screen.queryByRole('region', { name: 'Avisos' })).not.toBeInTheDocument()
    })
})
describe('CampanaConPendientes - resaltado por umbral', () => {
    it('justo debajo del umbral no se resalta', () => {
        renderizar(UMBRAL_PENDIENTES - 1)

        expect(campana()).toHaveAccessibleName(
            `Notificaciones: ${UMBRAL_PENDIENTES - 1} sin leer`,
        )
    })

    it('en el umbral se resalta y lo avisa con texto', () => {
        renderizar(UMBRAL_PENDIENTES)

        expect(campana()).toHaveAccessibleName(
            `Atención: ${UMBRAL_PENDIENTES} requisiciones pendientes`,
        )
    })

    it('por encima del umbral sigue resaltada', () => {
        renderizar(UMBRAL_PENDIENTES + 2)

        expect(campana()).toHaveAccessibleName(
            `Atención: ${UMBRAL_PENDIENTES + 2} requisiciones pendientes`,
        )
    })
})