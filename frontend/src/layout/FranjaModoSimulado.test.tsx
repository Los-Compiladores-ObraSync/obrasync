import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { FranjaModoSimulado } from './FranjaModoSimulado'

describe('FranjaModoSimulado', () => {
    afterEach(() => vi.unstubAllEnvs())

    it('aparece en modo simulado', () => {
        vi.stubEnv('VITE_USE_MOCKS', 'true')
        render(<FranjaModoSimulado />)
        expect(screen.getByRole('status')).toHaveTextContent('MODO SIMULADO')
    })

    it('no aparece con datos reales', () => {
        vi.stubEnv('VITE_USE_MOCKS', 'false')
        render(<FranjaModoSimulado />)
        expect(screen.queryByRole('status')).not.toBeInTheDocument()
    })
})