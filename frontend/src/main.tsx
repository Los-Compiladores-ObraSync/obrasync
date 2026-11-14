import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import { estaEnModoSimulado } from './mocks/modoSimulado'
import '@fontsource/barlow/400.css'
import '@fontsource/barlow/500.css'
import '@fontsource/barlow/700.css'
import './index.css'
import App from './App.tsx'

async function iniciar(): Promise<void> {
    if (estaEnModoSimulado()) {
        const { worker } = await import('./mocks/browser')
        await worker.start({ onUnhandledFrame: 'bypass' })
    }

  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </StrictMode>,
  )
}

void iniciar()
