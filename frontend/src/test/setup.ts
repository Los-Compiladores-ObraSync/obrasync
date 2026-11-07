import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { reiniciarObrasMock, reiniciarUsuariosMock } from '../mocks/handlers'
import { reiniciarUbicacionServiceParaTests } from '../obras/ubicacionService'
import { server } from '../mocks/server'

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => {
  cleanup()
  server.resetHandlers()
  reiniciarUsuariosMock()
  reiniciarObrasMock()
  reiniciarUbicacionServiceParaTests()
})
afterAll(() => server.close())
