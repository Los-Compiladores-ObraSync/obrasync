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
const CANTIDAD_USUARIOS_INICIAL = USUARIOS_MOCK.length

// Plantillas de ejemplo (las mismas que siembra el backend en #114).
function plantillaMock(n: number, nombre: string, descripcion: string, etapas: string[]) {
  return {
    id: `c1c2c3c4-0000-0000-0000-00000000000${n}`,
    nombre,
    descripcion,
    etapas: etapas.map((nombreEtapa, i) => ({
      id: `d1d2d3d4-0000-0000-000${n}-00000000000${i + 1}`,
      nombreEtapa,
      orden: i + 1,
    })),
  }
}

const PLANTILLAS_MOCK = [
  plantillaMock(
    1,
    'Edificio en Altura',
    'Plantilla estándar para desarrollo inmobiliario de múltiples pisos.',
    ['Cimientos y Subsuelo', 'Estructura de Hormigón', 'Mampostería', 'Instalaciones', 'Terminaciones'],
  ),
  plantillaMock(
    2,
    'Vivienda Unifamiliar',
    'Plantilla básica para casas residenciales de una o dos plantas.',
    ['Replanteo y Nivelación', 'Fundaciones', 'Muros y Cubierta', 'Instalaciones y Yesería', 'Terminaciones'],
  ),
  plantillaMock(
    3,
    'Nave Industrial',
    'Plantilla para galpones, centros de distribución y depósitos logísticos.',
    ['Movimiento de Suelos', 'Fundaciones y Bases', 'Estructura Metálica', 'Cerramientos y Cubierta', 'Pisos Industriales'],
  ),
]

/** Solo para tests: quita los usuarios que se crearon por POST /api/usuarios/registro. */
export function reiniciarUsuariosMock() {
  USUARIOS_MOCK.length = CANTIDAD_USUARIOS_INICIAL
}

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

interface ObraMock {
  id: string
  nombre: string
  ubicacion: string
  fechaInicio: string
  capatazId?: string
  capataz?: string
  etapas: { id: string; nombreEtapa: string; estado: string }[]
}

function crearObrasMock(): ObraMock[] {
  return [
  {
    id: 'b1b2c3d4-0000-0000-0000-0000000000a1',
    nombre: 'Torre Alvear',
    ubicacion: 'Av. Libertador 1200',
    fechaInicio: '2026-10-01T08:00:00Z',
    capatazId: 'a1b2c3d4-0000-0000-0000-000000000001',
    capataz: 'Juan Pérez',
    etapas: [
      { id: 'c1000000-0000-0000-0000-000000000001', nombreEtapa: 'Fundaciones', estado: 'Completada' },
      { id: 'c1000000-0000-0000-0000-000000000002', nombreEtapa: 'Estructura', estado: 'Pendiente' },
      { id: 'c1000000-0000-0000-0000-000000000003', nombreEtapa: 'Albañilería', estado: 'Pendiente' },
    ],
  },
  ]
}

// Las obras viven en memoria: crear, editar o dar de baja se refleja en el listado hasta recargar
// la página (#119). Las pruebas la reinician en setup.ts.
let obrasMock = crearObrasMock()

export function reiniciarObrasMock() {
  obrasMock = crearObrasMock()
}

// Requisiciones simuladas para la pantalla de aprobación (Compras).
// Se mantienen en memoria: aprobar una cambia su estado hasta recargar la página.
const OBRA_MOCK_ID = 'b1b2c3d4-0000-0000-0000-0000000000a1'

function crearRequisicionesMock() {
  return [
    {
      requisicionId: 'd1d2d3d4-0000-0000-0000-000000000011',
      numeroRequisicion: 'REQ-2026-0011',
      obraId: OBRA_MOCK_ID,
      etapa: 'Estructura',
      solicitanteId: 'a1b2c3d4-0000-0000-0000-000000000001',
      aprobadorId: null as string | null,
      rechazadorId: null as string | null,
      estado: 'Pendiente',
      observaciones: null,
      motivoRechazo: null as string | null,
      fechaCreacion: '2026-10-19T12:00:00Z',
      fechaAprobacion: null as string | null,
      fechaRechazo: null as string | null,
      items: [{ articuloId: MOCK_CATALOGO[0].articuloId, descripcion: MOCK_CATALOGO[0].descripcion, cantidad: 80, unidadMedida: MOCK_CATALOGO[0].unidadMedida }],
    },
    {
      requisicionId: 'd1d2d3d4-0000-0000-0000-000000000012',
      numeroRequisicion: 'REQ-2026-0012',
      obraId: OBRA_MOCK_ID,
      etapa: 'Fundaciones',
      solicitanteId: 'a1b2c3d4-0000-0000-0000-000000000001',
      aprobadorId: null as string | null,
      rechazadorId: null as string | null,
      estado: 'Pendiente',
      observaciones: 'Urgente',
      motivoRechazo: null as string | null,
      fechaCreacion: '2026-10-19T12:30:00Z',
      fechaAprobacion: null as string | null,
      fechaRechazo: null as string | null,
      items: [{ articuloId: MOCK_CATALOGO[0].articuloId, descripcion: MOCK_CATALOGO[0].descripcion, cantidad: 20, unidadMedida: MOCK_CATALOGO[0].unidadMedida }],
    },
    // Dos más, ya resueltas, para ver el historial con los tres estados (la 0010 la pidió otra persona).
    {
      requisicionId: 'd1d2d3d4-0000-0000-0000-000000000009',
      numeroRequisicion: 'REQ-2026-0009',
      obraId: OBRA_MOCK_ID,
      etapa: 'Fundaciones',
      solicitanteId: 'a1b2c3d4-0000-0000-0000-000000000001',
      aprobadorId: 'a1b2c3d4-0000-0000-0000-000000000002' as string | null,
      rechazadorId: null as string | null,
      estado: 'Aprobada',
      observaciones: null,
      motivoRechazo: null as string | null,
      fechaCreacion: '2026-10-15T12:00:00Z',
      fechaAprobacion: '2026-10-16T12:00:00Z' as string | null,
      fechaRechazo: null as string | null,
      items: [{ articuloId: MOCK_CATALOGO[1].articuloId, descripcion: MOCK_CATALOGO[1].descripcion, cantidad: 12, unidadMedida: MOCK_CATALOGO[1].unidadMedida }],
    },
    {
      requisicionId: 'd1d2d3d4-0000-0000-0000-000000000010',
      numeroRequisicion: 'REQ-2026-0010',
      obraId: OBRA_MOCK_ID,
      etapa: 'Estructura',
      solicitanteId: 'a1b2c3d4-0000-0000-0000-000000000099',
      aprobadorId: null as string | null,
      rechazadorId: 'a1b2c3d4-0000-0000-0000-000000000002' as string | null,
      estado: 'Rechazada',
      observaciones: 'Cantidad fuera de presupuesto',
      motivoRechazo: 'Cantidad fuera de presupuesto',
      fechaCreacion: '2026-10-17T12:00:00Z',
      fechaAprobacion: null as string | null,
      fechaRechazo: '2026-10-18T12:00:00Z' as string | null,
      items: [{ articuloId: MOCK_CATALOGO[2].articuloId, descripcion: MOCK_CATALOGO[2].descripcion, cantidad: 40, unidadMedida: MOCK_CATALOGO[2].unidadMedida }],
    },
  ]
}

let requisicionesMock = crearRequisicionesMock()

/** Solo para tests: vuelve las requisiciones simuladas a su estado inicial. */
export function reiniciarRequisicionesMock() {
  requisicionesMock = crearRequisicionesMock()
}

// Órdenes de compra pendientes de recibir (pantalla de recepción del Capataz).
// En memoria: al registrar una recepción total, la orden sale de la lista hasta recargar.
function crearOrdenesPendientesMock() {
  return [
    {
      ordenCompraId: 'e1e2e3e4-0000-0000-0000-000000000001',
      obraId: OBRA_MOCK_ID,
      items: [
        { articuloId: MOCK_CATALOGO[0].articuloId, descripcionArticulo: MOCK_CATALOGO[0].descripcion, cantidadEsperada: 80, cantidadRecibida: 0, unidadMedida: MOCK_CATALOGO[0].unidadMedida },
        { articuloId: MOCK_CATALOGO[1].articuloId, descripcionArticulo: MOCK_CATALOGO[1].descripcion, cantidadEsperada: 12, cantidadRecibida: 0, unidadMedida: MOCK_CATALOGO[1].unidadMedida },
      ],
    },
    {
      ordenCompraId: 'e1e2e3e4-0000-0000-0000-000000000002',
      obraId: OBRA_MOCK_ID,
      items: [
        { articuloId: MOCK_CATALOGO[2].articuloId, descripcionArticulo: MOCK_CATALOGO[2].descripcion, cantidadEsperada: 40, cantidadRecibida: 0, unidadMedida: MOCK_CATALOGO[2].unidadMedida },
      ],
    },
  ]
}

let ordenesPendientesMock = crearOrdenesPendientesMock()

/** Solo para tests: vuelve las órdenes pendientes simuladas a su estado inicial. */
export function reiniciarRecepcionesMock() {
  ordenesPendientesMock = crearOrdenesPendientesMock()
}

// Licitaciones simuladas para el portal de proveedores.
function crearLicitacionesMock() {
  return [
    {
      licitacionId: 'b1b2c3d4-1111-0000-0000-000000000001',
      requisicionId: 'd1d2d3d4-0000-0000-0000-000000000011',
      numeroRequisicion: 'REQ-2026-0011',
      obraId: OBRA_MOCK_ID,
      estado: 'Abierta',
      fechaCreacion: '2026-10-20T10:00:00Z',
      fechaLimiteCotizacion: '2026-11-20T18:00:00Z',
      items: [
        {
          articuloId: MOCK_CATALOGO[0].articuloId,
          descripcion: MOCK_CATALOGO[0].descripcion,
          cantidad: 80,
          unidadMedida: MOCK_CATALOGO[0].unidadMedida,
        },
        {
          articuloId: MOCK_CATALOGO[1].articuloId,
          descripcion: MOCK_CATALOGO[1].descripcion,
          cantidad: 15,
          unidadMedida: MOCK_CATALOGO[1].unidadMedida,
        },
      ],
    },
    {
      licitacionId: 'b1b2c3d4-1111-0000-0000-000000000002',
      requisicionId: 'd1d2d3d4-0000-0000-0000-000000000012',
      numeroRequisicion: 'REQ-2026-0012',
      obraId: OBRA_MOCK_ID,
      estado: 'Abierta',
      fechaCreacion: '2026-10-21T11:00:00Z',
      fechaLimiteCotizacion: '2026-11-25T18:00:00Z',
      items: [
        {
          articuloId: MOCK_CATALOGO[0].articuloId,
          descripcion: MOCK_CATALOGO[0].descripcion,
          cantidad: 20,
          unidadMedida: MOCK_CATALOGO[0].unidadMedida,
        },
        {
          articuloId: MOCK_CATALOGO[1].articuloId,
          descripcion: MOCK_CATALOGO[1].descripcion,
          cantidad: 10,
          unidadMedida: MOCK_CATALOGO[1].unidadMedida,
        },
        {
          articuloId: MOCK_CATALOGO[2].articuloId,
          descripcion: MOCK_CATALOGO[2].descripcion,
          cantidad: 30,
          unidadMedida: MOCK_CATALOGO[2].unidadMedida,
        },
      ],
    },
    {
      licitacionId: 'b1b2c3d4-1111-0000-0000-000000000003',
      requisicionId: 'd1d2d3d4-0000-0000-0000-000000000010',
      numeroRequisicion: 'REQ-2026-0010',
      obraId: OBRA_MOCK_ID,
      estado: 'Cerrada',
      fechaCreacion: '2026-10-15T10:00:00Z',
      fechaLimiteCotizacion: '2026-10-22T18:00:00Z',
      items: [
        {
          articuloId: MOCK_CATALOGO[2].articuloId,
          descripcion: MOCK_CATALOGO[2].descripcion,
          cantidad: 40,
          unidadMedida: MOCK_CATALOGO[2].unidadMedida,
        },
      ],
    },
  ]
}

interface CotizacionMock {
  cotizacionId: string
  licitacionId: string
  proveedorId: string
  flete?: number
  plazoEntregaDias?: number
  fechaCreacion: string
  items: Array<{ articuloId: string; precioUnitario: number }>
  puntajeTotal: number
  esGanadora: boolean
}

function crearCotizacionesMock(): CotizacionMock[] {
  return [
    {
      cotizacionId: 'c1111111-0000-0000-0000-000000000001',
      licitacionId: 'b1b2c3d4-1111-0000-0000-000000000001',
      proveedorId: 'f7a8b9c0-1111-2222-3333-444455556666',
      flete: 15000,
      plazoEntregaDias: 5,
      fechaCreacion: '2026-10-22T10:00:00Z',
      items: [
        { articuloId: MOCK_CATALOGO[0].articuloId, precioUnitario: 8000 },
        { articuloId: MOCK_CATALOGO[1].articuloId, precioUnitario: 14000 },
      ],
      puntajeTotal: 92.5,
      esGanadora: true,
    },
    {
      cotizacionId: 'c2222222-0000-0000-0000-000000000002',
      licitacionId: 'b1b2c3d4-1111-0000-0000-000000000001',
      proveedorId: 'e1a2b3c4-9999-8888-7777-666655554444',
      flete: 25000,
      plazoEntregaDias: 10,
      fechaCreacion: '2026-10-23T14:00:00Z',
      items: [
        { articuloId: MOCK_CATALOGO[0].articuloId, precioUnitario: 8500 },
        { articuloId: MOCK_CATALOGO[1].articuloId, precioUnitario: 16000 },
      ],
      puntajeTotal: 78.0,
      esGanadora: false,
    },
  ]
}

let licitacionesMock = crearLicitacionesMock()
let cotizacionesMock = crearCotizacionesMock()

/** Solo para tests: vuelve las licitaciones y cotizaciones simuladas a su estado inicial. */
export function reiniciarLicitacionesMock() {
  licitacionesMock = crearLicitacionesMock()
  cotizacionesMock = crearCotizacionesMock()
}

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

  // Contrato: POST /api/usuarios/password/recuperar → 200 { mensaje } | 400 ProblemDetails.
  // Como el backend real, responde siempre lo mismo exista o no la cuenta; solo falla (400)
  // si el email viene vacío. Es público: no necesita sesión.
  http.post('*/api/usuarios/password/recuperar', async ({ request }) => {
    const body = (await request.json()) as { email?: string }

    if (!body.email) {
      return HttpResponse.json(
        { title: 'Datos inválidos', status: 400, detail: 'El email es obligatorio.' },
        { status: 400 },
      )
    }
    return HttpResponse.json(
      { mensaje: 'Si el email existe, se envió el enlace de recuperación.' },
      { status: 200 },
    )
  }),

  // Contrato: POST /api/usuarios/password/restablecer → 200 { mensaje } | 400 ProblemDetails.
  // Es público: no necesita sesión. Para ver el error del enlace vencido, abrir
  // /restablecer-contrasena?email=capataz%40obrasync.com&token=token-vencido
  http.post('*/api/usuarios/password/restablecer', async ({ request }) => {
    const body = (await request.json()) as {
      email?: string
      token?: string
      nuevaPassword?: string
    }

    if (!body.email || !body.token || !body.nuevaPassword) {
      return HttpResponse.json(
        { title: 'Datos inválidos', status: 400, detail: 'Faltan datos para restablecer la contraseña.' },
        { status: 400 },
      )
    }
    if (body.token === 'token-vencido') {
      return HttpResponse.json(
        { title: 'Token inválido', status: 400, detail: 'El enlace venció o no es válido. Pedí uno nuevo.' },
        { status: 400 },
      )
    }
    if (body.nuevaPassword.length < 6) {
      return HttpResponse.json(
        { title: 'Contraseña inválida', status: 400, detail: 'La contraseña debe tener al menos 6 caracteres.' },
        { status: 400 },
      )
    }
    return HttpResponse.json({ mensaje: 'Contraseña restablecida exitosamente.' }, { status: 200 })
  }),

  // Contrato: POST /api/usuarios/cambiar-password → 200 { mensaje } | 400 | 401 ProblemDetails.
  // Requiere sesión (JWT). La contraseña actual de todos los usuarios simulados es MOCK_PASSWORD.
  // Para ver el 401 (sesión vencida) se puede mandar la contraseña actual "token-vencido".
  http.post('*/api/usuarios/cambiar-password', async ({ request }) => {
    if (!request.headers.get('Authorization')) {
      return HttpResponse.json(
        { title: 'No autenticado', status: 401, detail: 'Se requiere un token JWT valido para acceder a este recurso.' },
        { status: 401 },
      )
    }
    const body = (await request.json()) as {
      passwordActual?: string
      nuevaPassword?: string
      confirmarPassword?: string
    }

    if (body.passwordActual === 'token-vencido') {
      return HttpResponse.json(
        { title: 'No autenticado', status: 401, detail: 'Se requiere un token JWT valido para acceder a este recurso.' },
        { status: 401 },
      )
    }
    const invalido = (detail: string) =>
      HttpResponse.json({ title: 'Datos inválidos', status: 400, detail }, { status: 400 })

    if (!body.passwordActual || !body.nuevaPassword || !body.confirmarPassword) {
      return invalido('Faltan datos para cambiar la contraseña.')
    }
    if (body.nuevaPassword !== body.confirmarPassword) {
      return invalido('La confirmación de la contraseña no coincide.')
    }
    if (body.passwordActual === body.nuevaPassword) {
      return invalido('La nueva contraseña debe ser distinta de la actual.')
    }
    if (body.passwordActual !== MOCK_PASSWORD) {
      return invalido('La contraseña actual es incorrecta.')
    }
    if (body.nuevaPassword.length < 6) {
      return invalido('La contraseña debe tener al menos 6 caracteres.')
    }
    return HttpResponse.json({ mensaje: 'Contraseña actualizada exitosamente.' }, { status: 200 })
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
    const completo = body as {
      nombre: string
      ubicacion?: string
      fechaInicio?: string
      capatazId?: string
      fasesEtapas: string[]
    }
    const id = crypto.randomUUID()
    obrasMock.push({
      id,
      nombre: completo.nombre,
      ubicacion: completo.ubicacion ?? '',
      fechaInicio: completo.fechaInicio ?? new Date().toISOString(),
      capatazId: completo.capatazId,
      etapas: completo.fasesEtapas.map((nombreEtapa) => ({
        id: crypto.randomUUID(),
        nombreEtapa,
        estado: 'Pendiente',
      })),
    })
    // El contrato dice "string" pero no aclara qué contiene: devolvemos el id de la obra creada.
    return HttpResponse.text(id, { status: 200 })
  }),

  // Contrato: PUT /api/Obras/{id} → 200 | 400 | 403 | 404 | 409 (#118). No toca las etapas.
  // Para ver el 409 en pantalla, guardar una obra con el nombre "Obra Duplicada".
  http.put('*/api/obras/:id', async ({ request, params }) => {
    const obra = obrasMock.find((o) => o.id === params.id)
    if (!obra) {
      return HttpResponse.json(
        { title: 'No encontrada', status: 404, detail: 'La obra no existe.' },
        { status: 404 },
      )
    }
    const body = (await request.json()) as {
      nombre?: string
      ubicacion?: string
      fechaInicio?: string
      capatazId?: string
    }
    if (!body.nombre || !body.ubicacion || !body.fechaInicio || !body.capatazId) {
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
    obra.nombre = body.nombre
    obra.ubicacion = body.ubicacion
    obra.fechaInicio = body.fechaInicio
    obra.capatazId = body.capatazId
    return HttpResponse.json(obra, { status: 200 })
  }),

  // Contrato: DELETE /api/Obras/{id} → 204 | 403 | 404 (#118). Baja lógica: sale del listado.
  http.delete('*/api/obras/:id', ({ params }) => {
    const indice = obrasMock.findIndex((o) => o.id === params.id)
    if (indice === -1) {
      return HttpResponse.json(
        { title: 'No encontrada', status: 404, detail: 'La obra no existe.' },
        { status: 404 },
      )
    }
    obrasMock.splice(indice, 1)
    return new HttpResponse(null, { status: 204 })
  }),

  // Nominatim (OpenStreetMap), servicio externo para validar la ubicación de una obra (#113).
  // Para ver el aviso "no se encontró" en el navegador, escribir una dirección con "inexistente".
  http.get('https://nominatim.openstreetmap.org/search', ({ request }) => {
    const q = new URL(request.url).searchParams.get('q') ?? ''
    if (q.toLowerCase().includes('inexistente')) return HttpResponse.json([], { status: 200 })
    return HttpResponse.json(
      [{ place_id: 1, lat: '-34.6037', lon: '-58.3816', display_name: q }],
      { status: 200 },
    )
  }),

  // Contrato: GET /api/plantillas-obra → 200 PlantillaObraResponseDto[] (Admin, Comprador, Capataz)
  http.get('*/api/plantillas-obra', () => HttpResponse.json(PLANTILLAS_MOCK, { status: 200 })),

  // Contrato: GET /api/plantillas-obra/{id} → 200 PlantillaObraResponseDto | 404
  http.get('*/api/plantillas-obra/:id', ({ params }) => {
    const plantilla = PLANTILLAS_MOCK.find((p) => p.id === params.id)
    if (!plantilla) {
      return HttpResponse.json(
        { title: 'No encontrada', status: 404, detail: 'No existe la plantilla.' },
        { status: 404 },
      )
    }
    return HttpResponse.json(plantilla, { status: 200 })
  }),

  // Contrato: GET /api/usuarios/capataces → 200 [{ id, nombreCompleto }]
  http.get('*/api/usuarios/capataces', () => {
    return HttpResponse.json(
      USUARIOS_MOCK.filter((u) => u.rol === 'Capataz').map((u) => ({
        id: u.id,
        nombreCompleto: u.nombreCompleto,
      })),
      { status: 200 },
    )
  }),

  // Contrato: POST /api/usuarios/registro → 201 AuthResponseDto | 400 | 403 | 409 (email repetido).
  // Agrega el usuario a la lista del mock, así el capataz nuevo aparece en GET /capataces.
  http.post('*/api/usuarios/registro', async ({ request }) => {
    const body = (await request.json()) as {
      nombreCompleto?: string
      email?: string
      password?: string
      rol?: string
    }
    if (!body.nombreCompleto || !body.email || !body.password || (body.password?.length ?? 0) < 6) {
      return HttpResponse.json(
        { title: 'Datos inválidos', status: 400, detail: 'Faltan datos del usuario.' },
        { status: 400 },
      )
    }
    if (USUARIOS_MOCK.some((u) => u.email === body.email)) {
      return HttpResponse.json(
        {
          title: 'Conflicto',
          status: 409,
          detail: 'Ya existe un usuario registrado con este correo electrónico.',
        },
        { status: 409 },
      )
    }
    const nuevo = {
      id: `a1b2c3d4-0000-0000-0000-${String(USUARIOS_MOCK.length + 1).padStart(12, '0')}`,
      nombreCompleto: body.nombreCompleto,
      email: body.email,
      rol: body.rol ?? 'Capataz',
    }
    USUARIOS_MOCK.push(nuevo)
    return HttpResponse.json(
      { id: nuevo.id, nombreCompleto: nuevo.nombreCompleto, email: nuevo.email, token: MOCK_TOKEN, roles: [nuevo.rol] },
      { status: 201 },
    )
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
    return HttpResponse.json(obrasMock, { status: 200 })
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

  // Contrato: GET /api/requisiciones?estado=&obraId= → 200 RequisicionResponseDto[]
  http.get('*/api/requisiciones', ({ request }) => {
    const url = new URL(request.url)
    const estado = url.searchParams.get('estado')
    const obraId = url.searchParams.get('obraId')
    // El backend real compara el estado sin distinguir mayúsculas.
    const lista = requisicionesMock.filter(
      (r) =>
        (!estado || r.estado.toLowerCase() === estado.toLowerCase()) &&
        (!obraId || r.obraId === obraId),
    )
    return HttpResponse.json(lista, { status: 200 })
  }),

  // Contrato: GET /api/requisiciones/{id} → 200 RequisicionResponseDto | 404 ProblemDetails
  http.get('*/api/requisiciones/:id', ({ params }) => {
    const req = requisicionesMock.find((r) => r.requisicionId === params.id)
    if (!req) {
      return HttpResponse.json(
        { title: 'No encontrada', status: 404, detail: 'La requisición no existe.' },
        { status: 404 },
      )
    }
    return HttpResponse.json(req, { status: 200 })
  }),

  // Contrato: POST /api/requisiciones/{id}/aprobar → 200 | 404 | 409 ProblemDetails
  http.post('*/api/requisiciones/:id/aprobar', ({ params }) => {
    const req = requisicionesMock.find((r) => r.requisicionId === params.id)
    if (!req) {
      return HttpResponse.json(
        { title: 'No encontrada', status: 404, detail: 'La requisición no existe.' },
        { status: 404 },
      )
    }
    if (req.estado !== 'Pendiente') {
      return HttpResponse.json(
        { title: 'Conflicto', status: 409, detail: 'Solo se pueden aprobar requisiciones pendientes.' },
        { status: 409 },
      )
    }
    req.estado = 'Aprobada'
    req.aprobadorId = 'a1b2c3d4-0000-0000-0000-000000000002'
    req.fechaAprobacion = new Date().toISOString()
    return HttpResponse.json(req, { status: 200 })
  }),

  // Contrato: POST /api/requisiciones/{id}/rechazar → 200 | 400 | 404 | 409 ProblemDetails
  http.post('*/api/requisiciones/:id/rechazar', async ({ params, request }) => {
    const req = requisicionesMock.find((r) => r.requisicionId === params.id)
    if (!req) {
      return HttpResponse.json(
        { title: 'No encontrada', status: 404, detail: 'La requisición ya no existe. Recargá la lista.' },
        { status: 404 },
      )
    }

    const body = (await request.json().catch(() => ({}))) as { motivo?: string }
    const motivoTrimmed = typeof body.motivo === 'string' ? body.motivo.trim() : ''
    if (!motivoTrimmed || (body.motivo?.length ?? 0) > 500) {
      return HttpResponse.json(
        {
          title: 'Datos inválidos',
          status: 400,
          detail: 'El motivo de rechazo es obligatorio y debe tener como máximo 500 caracteres.',
        },
        { status: 400 },
      )
    }

    if (req.estado !== 'Pendiente') {
      return HttpResponse.json(
        { title: 'Conflicto', status: 409, detail: 'Solo se pueden rechazar requisiciones pendientes.' },
        { status: 409 },
      )
    }

    req.estado = 'Rechazada'
    req.motivoRechazo = body.motivo ?? ''
    req.rechazadorId = 'a1b2c3d4-0000-0000-0000-000000000002'
    req.fechaRechazo = new Date().toISOString()
    return HttpResponse.json(req, { status: 200 })
  }),

  // Contrato: GET /api/recepciones/ordenes-pendientes?obraId= → 200 OrdenPendienteDto[]
  http.get('*/api/recepciones/ordenes-pendientes', ({ request }) => {
    const obraId = new URL(request.url).searchParams.get('obraId')
    const lista = ordenesPendientesMock.filter((o) => !obraId || o.obraId === obraId)
    return HttpResponse.json(lista, { status: 200 })
  }),

  // Contrato: POST /api/recepciones → 201 RecepcionDto | 400 | 409 ProblemDetails
  // Solo acepta recepciones TOTALES (como el backend en este sprint). Errores de red, 403 y 409
  // se configuran en los tests con server.use().
  http.post('*/api/recepciones', async ({ request }) => {
    const body = (await request.json()) as {
      obraId?: string
      ordenCompraId?: string
      numeroRemito?: string
      fechaRecepcion?: string
      observaciones?: string | null
      detalles?: { articuloId: string; cantidadEsperada: number; cantidadRecibida: number }[]
    }
    const orden = ordenesPendientesMock.find((o) => o.ordenCompraId === body.ordenCompraId)
    if (!orden) {
      return HttpResponse.json(
        { title: 'Orden inexistente', status: 400, detail: 'La orden de compra no existe o ya fue recibida.' },
        { status: 400 },
      )
    }
    if (!body.numeroRemito?.trim() || !body.fechaRecepcion || !body.detalles?.length) {
      return HttpResponse.json(
        { title: 'Datos inválidos', status: 400, detail: 'Faltan datos de la recepción.' },
        { status: 400 },
      )
    }
    if (body.detalles.some((d) => d.cantidadRecibida !== d.cantidadEsperada)) {
      return HttpResponse.json(
        { title: 'Entrega parcial', status: 400, detail: 'La recepción parcial todavía no está disponible.' },
        { status: 400 },
      )
    }

    ordenesPendientesMock = ordenesPendientesMock.filter((o) => o.ordenCompraId !== orden.ordenCompraId)
    return HttpResponse.json(
      {
        id: 'f1f2f3f4-0000-0000-0000-000000000001',
        obraId: orden.obraId,
        ordenCompraId: orden.ordenCompraId,
        recepcionadoPorUsuarioId: 'a1b2c3d4-0000-0000-0000-000000000001',
        numeroRemito: body.numeroRemito.trim(),
        fechaRecepcion: body.fechaRecepcion,
        estado: 3,
        observaciones: body.observaciones ?? null,
        createdAt: new Date().toISOString(),
        updatedAt: null,
      },
      { status: 201 },
    )
  }),

  // Contrato: GET /api/licitaciones → 200 LicitacionResponseDto[]
  // Al Proveedor el backend ya le devuelve únicamente las abiertas.
  // yaCotizada: el backend real solo lo completa para el Proveedor (con el 'sub' de su token). El mock
  // no lee el token, así que lo calcula siempre para el proveedor de prueba.
  http.get('*/api/licitaciones', () => {
    const proveedorId = USUARIOS_MOCK.find((u) => u.rol === 'Proveedor')?.id
    const abiertas = licitacionesMock
      .filter((l) => l.estado === 'Abierta')
      .map((l) => ({
        ...l,
        yaCotizada: cotizacionesMock.some(
          (c) => c.licitacionId === l.licitacionId && c.proveedorId === proveedorId,
        ),
      }))
    return HttpResponse.json(abiertas, { status: 200 })
  }),

  // Contrato: GET /api/licitaciones/{id} → 200 LicitacionResponseDto | 404 ProblemDetails
  http.get('*/api/licitaciones/:id', ({ params }) => {
    const lic = licitacionesMock.find((l) => l.licitacionId === params.id)
    if (!lic) {
      return HttpResponse.json(
        { title: 'No encontrada', status: 404, detail: 'La licitación no existe.' },
        { status: 404 },
      )
    }
    return HttpResponse.json(lic, { status: 200 })
  }),

  // Contrato: POST /api/cotizaciones → 201 CotizacionResponseDto | 400 | 404 | 409 ProblemDetails
  http.post('*/api/cotizaciones', async ({ request }) => {
    const body = (await request.json().catch(() => ({}))) as {
      licitacionId?: string
      proveedorId?: string
      flete?: number
      plazoEntregaDias?: number
      items?: Array<{ articuloId: string; precioUnitario: number }>
    }

    const lic = licitacionesMock.find((l) => l.licitacionId === body.licitacionId)
    if (!lic) {
      return HttpResponse.json(
        { title: 'No encontrada', status: 404, detail: 'La licitación no existe.' },
        { status: 404 },
      )
    }

    if (lic.estado !== 'Abierta') {
      return HttpResponse.json(
        {
          title: 'Licitación cerrada',
          status: 400,
          detail: 'La licitación ya se encuentra cerrada y no recibe cotizaciones.',
        },
        { status: 400 },
      )
    }

    const yaCotizo = cotizacionesMock.some(
      (c) => c.licitacionId === body.licitacionId && c.proveedorId === body.proveedorId,
    )
    if (yaCotizo) {
      return HttpResponse.json(
        {
          title: 'Cotización duplicada',
          status: 409,
          detail: 'Ya cotizaste para esta licitación.',
        },
        { status: 409 },
      )
    }

    if (!body.items || body.items.length === 0 || body.items.some((it) => it.precioUnitario <= 0)) {
      return HttpResponse.json(
        {
          title: 'Datos inválidos',
          status: 400,
          detail: 'Todos los ítems deben tener un precio unitario mayor a cero.',
        },
        { status: 400 },
      )
    }

    const nuevaCotizacion: CotizacionMock = {
      cotizacionId: `cot-${Date.now()}-${Math.random().toString(16).slice(2)}`,
      licitacionId: lic.licitacionId,
      proveedorId: body.proveedorId ?? '',
      flete: body.flete ?? 0,
      plazoEntregaDias: body.plazoEntregaDias ?? 0,
      fechaCreacion: new Date().toISOString(),
      items: body.items,
      puntajeTotal: 0,
      esGanadora: false,
    }

    cotizacionesMock.push(nuevaCotizacion)
    return HttpResponse.json(nuevaCotizacion, { status: 201 })
  }),

  // Contrato: GET /api/cotizaciones?licitacionId={guid} → 200 CotizacionResponseDto[] | 400 ProblemDetails
  http.get('*/api/cotizaciones', ({ request }) => {
    const url = new URL(request.url)
    const licitacionId = url.searchParams.get('licitacionId')
    if (!licitacionId) {
      return HttpResponse.json(
        {
          title: 'Datos inválidos',
          status: 400,
          detail: 'El identificador de la licitación es obligatorio.',
        },
        { status: 400 },
      )
    }

    const filtradas = cotizacionesMock.filter((c) => c.licitacionId === licitacionId)
    return HttpResponse.json(filtradas, { status: 200 })
  }),

  // Contrato: POST /api/licitaciones/{licitacionId}/adjudicar con body {"cotizacionId": "<guid>"}
  // → 200 OrdenCompraResponseDto | 400 | 404 | 409 ProblemDetails
  http.post('*/api/licitaciones/:licitacionId/adjudicar', async ({ params, request }) => {
    const licitacionId = params.licitacionId as string
    const lic = licitacionesMock.find((l) => l.licitacionId === licitacionId)
    if (!lic) {
      return HttpResponse.json(
        {
          title: 'Recurso no encontrado',
          status: 404,
          detail: `No se encontró la licitación con ID ${licitacionId}.`,
        },
        { status: 404 },
      )
    }

    if (lic.estado !== 'Abierta') {
      return HttpResponse.json(
        {
          title: 'Conflicto en la adjudicación',
          status: 409,
          detail: 'La licitación ya se encuentra adjudicada o cerrada.',
        },
        { status: 409 },
      )
    }

    const body = (await request.json().catch(() => ({}))) as { cotizacionId?: string }
    if (!body.cotizacionId) {
      return HttpResponse.json(
        {
          title: 'Solicitud inválida',
          status: 400,
          detail: 'El identificador de la cotización es obligatorio.',
        },
        { status: 400 },
      )
    }

    const cot = cotizacionesMock.find((c) => c.cotizacionId === body.cotizacionId)
    if (!cot) {
      return HttpResponse.json(
        {
          title: 'Recurso no encontrado',
          status: 404,
          detail: `No se encontró la cotización con ID ${body.cotizacionId}.`,
        },
        { status: 404 },
      )
    }

    if (cot.licitacionId !== licitacionId) {
      return HttpResponse.json(
        {
          title: 'Solicitud inválida',
          status: 400,
          detail: 'La cotización no pertenece a la licitación especificada.',
        },
        { status: 400 },
      )
    }

    lic.estado = 'Adjudicada'

    const ordenCompra = {
      id: `oc-${Date.now()}-${Math.random().toString(16).slice(2, 8)}`,
      licitacionId: lic.licitacionId,
      cotizacionId: cot.cotizacionId,
      estado: 'Emitida',
      fechaEmision: new Date().toISOString(),
    }

    return HttpResponse.json(ordenCompra, { status: 200 })
  }),
]
