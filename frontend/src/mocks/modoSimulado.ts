export function estaEnModoSimulado(): boolean {
    return import.meta.env.DEV && import.meta.env.VITE_USE_MOCKS === 'true'
}