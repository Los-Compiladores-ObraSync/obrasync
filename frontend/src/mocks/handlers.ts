import { http, HttpResponse } from 'msw'
import type { RequestHandler } from 'msw'

// Token simulado con la estructura real de JWT (header.payload.firma).
// Claims según contrato: sub, email, name, role.
// La firma es falsa: el frontend no la valida, solo la reenvía.
const MOCK_TOKEN =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9' +
  '.eyJzdWIiOiJhMWIyYzNkNC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDEiLCJlbWFpbCI6ImNhcGF0YXpAb2JyYXN5bmMuY29tIiwibmFtZSI6Ikp1YW4gUMOpcmV6Iiwicm9sZSI6IkNhcGF0YXoifQ' +
  '.MOCK_FIRMA_NO_VALIDA'

// Credencial de prueba para desarrollo con mocks.
const MOCK_EMAIL = 'capataz@obrasync.com'
const MOCK_PASSWORD = 'Test1234!'

export const handlers: RequestHandler[] = [
  // Contrato: POST /api/usuarios/login → 200 AuthResponseDto | 401 ProblemDetails
  // Se usa ruta relativa porque en el browser Vite proxea /api/* → http://localhost:5000.
  http.post('/api/usuarios/login', async ({ request }) => {
    const body = (await request.json()) as { email?: string; password?: string }

    if (body.email === MOCK_EMAIL && body.password === MOCK_PASSWORD) {
      return HttpResponse.json(
        {
          id: 'a1b2c3d4-0000-0000-0000-000000000001',
          nombreCompleto: 'Juan Pérez',
          email: MOCK_EMAIL,
          token: MOCK_TOKEN,
          roles: ['Capataz'],
        },
        { status: 200 },
      )
    }

    // Formato ProblemDetails, igual que devuelve el backend real (AddProblemDetails()).
    return HttpResponse.json(
      {
        type: 'https://tools.ietf.org/html/rfc7235#section-3.1',
        title: 'Credenciales inválidas',
        status: 401,
        detail: 'Email o contraseña incorrectos.',
      },
      { status: 401 },
    )
  }),
]
