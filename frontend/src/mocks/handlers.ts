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

const MOCK_CATALOGO = [
  {
    articuloId: 'c1c2c3c4-0000-0000-0000-000000000001',
    codigo: 'MAT-CEM-001',
    descripcion: 'Cemento Portland Especial 50kg',
    unidadMedida: 'BOLSA',
    moneda: 'ARS',
    precioReferencia: 8500,
    activo: true,
  },
  {
    articuloId: 'c1c2c3c4-0000-0000-0000-000000000002',
    codigo: 'MAT-ARE-001',
    descripcion: 'Arena Fina a Granel',
    unidadMedida: 'M3',
    moneda: 'ARS',
    precioReferencia: 15000,
    activo: true,
  },
  {
    articuloId: 'c1c2c3c4-0000-0000-0000-000000000003',
    codigo: 'MAT-CAL-001',
    descripcion: 'Cal Hidráulica 25kg',
    unidadMedida: 'BOLSA',
    moneda: 'ARS',
    precioReferencia: 5200,
    activo: true,
  },
]

const MOCK_OBRAS = [
  {
    id: 'b1b2c3d4-0000-0000-0000-0000000000a1',
    nombre: 'Torre Alvear',
    ubicacion: 'Av. Libertador 1200',
    fechaInicio: '2026-10-01T08:00:00Z',
    capataz: 'Juan Pérez',
    etapas: [
      { id: 1, nombreEtapa: 'Fundaciones', estado: 'Completada' },
      { id: 2, nombreEtapa: 'Estructura', estado: 'Pendiente' },
      { id: 3, nombreEtapa: 'Albañilería', estado: 'Pendiente' },
    ],
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

  // Contrato: POST /api/obras → 200 (string) | 400 | 403 | 409.
  // Solo para desarrollo y pruebas. Para ver el error 409 en pantalla, crear una obra
  // llamada "Obra Duplicada".
  http.post('*/api/obras', async ({ request }) => {
    const body = (await request.json()) as { nombre?: string; fasesEtapas?: string[] }

    if (!body.nombre || !body.fasesEtapas?.length) {
      return HttpResponse.json(
        { title: 'Datos inválidos', status: 400, detail: 'Faltan datos de la obra.' },
        { status: 400 },
      )
    }
    if (body.nombre === 'Obra Duplicada') {
      return HttpResponse.json(
        { title: 'Conflicto', status: 409, detail: 'Ya existe una obra con ese nombre.' },
        { status: 409 },
      )
    }
    // El contrato dice "string" pero no aclara qué contiene: acá devolvemos un id de ejemplo.
    return HttpResponse.text('b1b2c3d4-0000-0000-0000-0000000000a1', { status: 200 })
  }),

  // Contrato: GET /api/catalogo/insumos → 200 InsumoCatalogoDto[]
  http.get('*/api/catalogo/insumos', ({ request }) => {
    const url = new URL(request.url)
    const search = url.searchParams.get('search')?.toLowerCase()
    const soloActivos = url.searchParams.get('soloActivos') !== 'false'

    let resultado = MOCK_CATALOGO
    if (soloActivos) {
      resultado = resultado.filter((i) => i.activo)
    }
    if (search) {
      resultado = resultado.filter(
        (i) =>
          i.codigo.toLowerCase().includes(search) ||
          i.descripcion.toLowerCase().includes(search),
      )
    }
    return HttpResponse.json(resultado, { status: 200 })
  }),

  // Contrato: GET /api/obras → 200 Obra[] (Admin, Comprador, Capataz)
  http.get('*/api/obras', () => {
    return HttpResponse.json(MOCK_OBRAS, { status: 200 })
  }),

  // Contrato: POST /api/requisiciones → 201 RequisicionResponseDto | 400 ProblemDetails
  // Responde 201 en éxito o 400 si faltan ítems o cantidad <= 0.
  // Casos de 403 y red caída se configuran en los tests con server.use().
  http.post('*/api/requisiciones', async ({ request }) => {
    const body = (await request.json()) as {
      obraId?: string
      etapa?: string
      observaciones?: string
      items?: Array<{ articuloId: string; cantidad: number }>
    }

    if (!body.items || body.items.length === 0) {
      return HttpResponse.json(
        {
          title: 'Datos de requisición inválidos',
          status: 400,
          detail: 'La requisición debe tener al menos un ítem.',
        },
        { status: 400 },
      )
    }

    const cantidadInvalida = body.items.some((it) => !it.cantidad || it.cantidad <= 0)
    if (cantidadInvalida) {
      return HttpResponse.json(
        {
          title: 'Datos de requisición inválidos',
          status: 400,
          detail: 'La cantidad de cada ítem debe ser mayor a cero.',
        },
        { status: 400 },
      )
    }

    const itemsRespuesta = body.items.map((item) => {
      const insumo = MOCK_CATALOGO.find((c) => c.articuloId === item.articuloId)
      return {
        articuloId: item.articuloId,
        descripcion: insumo ? insumo.descripcion : 'Insumo de obra',
        cantidad: item.cantidad,
        unidadMedida: insumo ? insumo.unidadMedida : 'UNIDAD',
      }
    })

    return HttpResponse.json(
      {
        requisicionId: 'd1d2d3d4-0000-0000-0000-000000000001',
        numeroRequisicion: 'REQ-2026-0001',
        obraId: body.obraId ?? null,
        etapa: body.etapa ?? null,
        solicitanteId: 'a1b2c3d4-0000-0000-0000-000000000001',
        aprobadorId: null,
        estado: 'Pendiente',
        observaciones: body.observaciones ?? null,
        fechaCreacion: new Date().toISOString(),
        fechaAprobacion: null,
        items: itemsRespuesta,
      },
      { status: 201 },
    )
  }),
]
