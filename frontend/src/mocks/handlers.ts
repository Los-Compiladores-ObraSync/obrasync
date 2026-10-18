import { http, HttpResponse } from 'msw'
import type { RequestHandler } from 'msw'

// Token simulado con la estructura real de JWT (header.payload.firma).
// La firma es falsa y los claims son de ejemplo: el frontend no decodifica el token,
// solo lo reenvía. El usuario y el rol salen del cuerpo de la respuesta (roles[]).
const MOCK_TOKEN =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9' +
  '.eyJzdWIiOiJhMWIyYzNkNC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDEiLCJlbWFpbCI6ImNhcGF0YXpAb2JyYXN5bmMuY29tIiwibmFtZSI6Ikp1YW4gUMOpcmV6Iiwicm9sZSI6IkNhcGF0YXoifQ' +
  '.MOCK_FIRMA_NO_VALIDA'

// Una contraseña de prueba para todos los usuarios simulados.
const MOCK_PASSWORD = 'Test1234!'

// Un usuario simulado por rol, SOLO para desarrollar y probar con mocks.
// No son los datos de prueba oficiales de la demo (eso es la tarjeta #30).
const USUARIOS_MOCK = [
  {
    id: 'a1b2c3d4-0000-0000-0000-000000000001',
    nombreCompleto: 'Juan Pérez',
    email: 'capataz@obrasync.com',
    rol: 'Capataz',
  },
  {
    id: 'a1b2c3d4-0000-0000-0000-000000000002',
    nombreCompleto: 'Marta Gómez',
    email: 'comprador@obrasync.com',
    rol: 'Comprador',
  },
  {
    id: 'a1b2c3d4-0000-0000-0000-000000000003',
    nombreCompleto: 'Carlos Ruiz',
    email: 'proveedor@obrasync.com',
    rol: 'Proveedor',
  },
  {
    id: 'a1b2c3d4-0000-0000-0000-000000000004',
    nombreCompleto: 'Admin ObraSync',
    email: 'admin@obrasync.com',
    rol: 'Admin',
  },
]

export const handlers: RequestHandler[] = [
  // Contrato: POST /api/usuarios/login → 200 AuthResponseDto | 401 ProblemDetails
  // Se usa '*/api/usuarios/login' para interceptar la ruta tanto relativa (en browser con proxy)
  // como con origen explícito http://localhost:5000 (en Vitest).
  http.post('*/api/usuarios/login', async ({ request }) => {
    const body = (await request.json()) as { email?: string; password?: string }

    const usuario = USUARIOS_MOCK.find((u) => u.email === body.email)

    if (usuario && body.password === MOCK_PASSWORD) {
      return HttpResponse.json(
        {
          id: usuario.id,
          nombreCompleto: usuario.nombreCompleto,
          email: usuario.email,
          token: MOCK_TOKEN,
          roles: [usuario.rol],
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
