import { http, HttpResponse } from 'msw'
import type { RequestHandler } from 'msw'
import { hoyComoTexto } from '../obras/fechaEnPalabras'
import { MOTIVOS_PROBLEMA, type RecepcionHistorial } from '../recepciones/recepcionesTipos'

// Token simulado con la estructura real de JWT (header.payload.firma).
// La firma es falsa y los claims son de ejemplo: el frontend no decodifica el token,
// solo lo reenvía. El usuario y el rol salen del cuerpo de la respuesta (roles[]).
export const MOCK_TOKEN_CAPATAZ =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9' +
  '.eyJzdWIiOiJhMWIyYzNkNC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDEiLCJlbWFpbCI6ImNhcGF0YXpAb2JyYXN5bmMuY29tIiwibmFtZSI6Ikp1YW4gUMOpcmV6Iiwicm9sZSI6IkNhcGF0YXoifQ' +
  '.MOCK_FIRMA_NO_VALIDA'
export const MOCK_TOKEN_COMPRADOR =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9' +
  '.eyJzdWIiOiJhMWIyYzNkNC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDIiLCJlbWFpbCI6ImNvbXByYWRvckBvYnJhc3luYy5jb20iLCJyb2xlIjoiQ29tcHJhZG9yIn0' +
  '.MOCK_FIRMA_NO_VALIDA'
export const MOCK_TOKEN_PROVEEDOR =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9' +
  '.eyJzdWIiOiJhMWIyYzNkNC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDMiLCJlbWFpbCI6InByb3ZlZWRvckBvYnJhc3luYy5jb20iLCJyb2xlIjoiUHJvdmVlZG9yIn0' +
  '.MOCK_FIRMA_NO_VALIDA'

// Una contraseña de prueba para todos los usuarios simulados.
const MOCK_PASSWORD = 'Password123!'

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

/** Solo para tests: quita los usuarios que se crearon por POST /api/usuarios. */
export function reiniciarUsuariosMock() {
  USUARIOS_MOCK.length = CANTIDAD_USUARIOS_INICIAL
}

const MOCK_CATALOGO = [
  {
    articuloId: 'c1c2c3c4-0000-0000-0000-000000000001',
    codigo: 'MAT-CEM-001',
    descripcion: 'Cemento Portland Especial 25kg',
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
    {
      requisicionId: 'd1d2d3d4-0000-0000-0000-000000000013',
      numeroRequisicion: 'REQ-2026-0013', obraId: OBRA_MOCK_ID, etapa: 'Fundaciones',
      solicitanteId: 'a1b2c3d4-0000-0000-0000-000000000001', aprobadorId: null as string | null,
      rechazadorId: 'a1b2c3d4-0000-0000-0000-000000000002' as string | null,
      estado: 'Rechazada', observaciones: 'Revisar la cantidad', motivoRechazo: 'Falta justificar el volumen',
      fechaCreacion: '2026-10-20T12:00:00Z', fechaAprobacion: null as string | null,
      fechaRechazo: '2026-10-21T12:00:00Z' as string | null,
      items: [{ articuloId: MOCK_CATALOGO[0].articuloId, descripcion: MOCK_CATALOGO[0].descripcion, cantidad: 25, unidadMedida: MOCK_CATALOGO[0].unidadMedida }],
    },
  ]
}

let requisicionesMock = crearRequisicionesMock()

/** Solo para tests: vuelve las requisiciones simuladas a su estado inicial. */
export function reiniciarRequisicionesMock() {
  requisicionesMock = crearRequisicionesMock()
}

// Órdenes de compra pendientes de recibir (pantalla de recepción del Capataz).
// En memoria: mantiene saldos e historial mientras la demo está abierta.
function crearOrdenesPendientesMock() {
  return [
    {
      ordenCompraId: 'e1e2e3e4-0000-0000-0000-000000000001',
      obraId: OBRA_MOCK_ID,
      // Despachada por el Proveedor (#216), con fecha estimada y observación (#217).
      fechaDespacho: '2026-11-20T15:00:00Z' as string | null,
      fechaEstimadaLlegada: '2026-11-23T12:00:00Z' as string | null,
      observacionDespacho: 'Llega en camión chico, antes de las 10.' as string | null,
      tieneProblemaAbierto: false as boolean,
      items: [
        { articuloId: MOCK_CATALOGO[0].articuloId, descripcionArticulo: MOCK_CATALOGO[0].descripcion, cantidadEsperada: 80, cantidadRecibida: 0, cantidadPendiente: 80, unidadMedida: MOCK_CATALOGO[0].unidadMedida },
        { articuloId: MOCK_CATALOGO[1].articuloId, descripcionArticulo: MOCK_CATALOGO[1].descripcion, cantidadEsperada: 12, cantidadRecibida: 0, cantidadPendiente: 12, unidadMedida: MOCK_CATALOGO[1].unidadMedida },
      ],
    },
    {
      ordenCompraId: 'e1e2e3e4-0000-0000-0000-000000000002',
      obraId: OBRA_MOCK_ID,
      // Despachada, pero el Proveedor no informó fecha estimada ni observación.
      fechaDespacho: '2026-11-21T15:00:00Z' as string | null,
      fechaEstimadaLlegada: null as string | null,
      observacionDespacho: null as string | null,
      tieneProblemaAbierto: false as boolean,
      items: [
        { articuloId: MOCK_CATALOGO[2].articuloId, descripcionArticulo: MOCK_CATALOGO[2].descripcion, cantidadEsperada: 40, cantidadRecibida: 0, cantidadPendiente: 40, unidadMedida: MOCK_CATALOGO[2].unidadMedida },
      ],
    },
  ]
}

let ordenesPendientesMock = crearOrdenesPendientesMock()
let recepcionesMock: Record<string, RecepcionHistorial[]> = {}
let ordenesCompletadasMock = new Set<string>()
// Órdenes ya recibidas por completo (salen de las pendientes): se guardan para poder consultar su historial (#224).
let ordenesCompletasMock = new Map<string, (typeof ordenesPendientesMock)[number]>()
// Fotos de evidencia subidas (#224): la clave es "recepcionId/fotoId".
let fotosEvidenciaMock = new Map<string, { contentType: string; bytes: ArrayBuffer }>()

/** Solo para tests: vuelve las órdenes pendientes simuladas a su estado inicial. */
export function reiniciarRecepcionesMock(opciones: { cantidadCementoInicial?: number } = {}) {
  ordenesPendientesMock = crearOrdenesPendientesMock()
  if (opciones.cantidadCementoInicial !== undefined) {
    const cemento = ordenesPendientesMock[0].items[0]
    cemento.cantidadEsperada = opciones.cantidadCementoInicial
    cemento.cantidadPendiente = opciones.cantidadCementoInicial
  }
  recepcionesMock = {}
  ordenesCompletadasMock = new Set()
  ordenesCompletasMock = new Map()
  fotosEvidenciaMock = new Map()
}

// Órdenes de compra simuladas para la pantalla de despacho de Compras (#205).
// Dos sin despachar y una ya despachada, así la demo muestra los dos estados.
export const ORDEN_COMPRA_SIN_DESPACHAR_1_ID = 'c1c2c3c4-0000-0000-0000-000000000001'
export const ORDEN_COMPRA_SIN_DESPACHAR_2_ID = 'c1c2c3c4-0000-0000-0000-000000000002'
export const ORDEN_COMPRA_DESPACHADA_ID = 'c1c2c3c4-0000-0000-0000-000000000003'

function crearOrdenesCompraMock() {
  const base = {
    licitacionId: 'b1b2c3d4-1111-0000-0000-000000000001',
    cotizacionId: 'f1f2f3f4-0000-0000-0000-000000000001',
    obraId: OBRA_MOCK_ID,
    proveedorId: 'a1b2c3d4-0000-0000-0000-000000000003',
  }
  // Campos del re-despacho del remanente (#248): se tipan acá para que el mock pueda cambiarlos.
  const despacho = {
    numeroDespacho: undefined as number | undefined,
    puedeRedespachar: undefined as boolean | undefined,
    tieneRecepcionParcialPendiente: undefined as boolean | undefined,
  }
  return [
    {
      ...base,
      ...despacho,
      id: ORDEN_COMPRA_SIN_DESPACHAR_1_ID,
      estado: 'Emitida',
      fechaEmision: '2026-11-18T12:00:00Z',
      fueDespachada: false,
      fechaDespacho: null as string | null,
      fechaEstimadaLlegada: null as string | null,
      observacion: null as string | null,
    },
    {
      ...base,
      ...despacho,
      id: ORDEN_COMPRA_SIN_DESPACHAR_2_ID,
      estado: 'Emitida',
      fechaEmision: '2026-11-17T12:00:00Z',
      fueDespachada: false,
      fechaDespacho: null as string | null,
      fechaEstimadaLlegada: null as string | null,
      observacion: null as string | null,
    },
    {
      ...base,
      ...despacho,
      id: ORDEN_COMPRA_DESPACHADA_ID,
      estado: 'Despachada',
      fechaEmision: '2026-11-12T12:00:00Z',
      fueDespachada: true,
      fechaDespacho: '2026-11-13T12:00:00Z' as string | null,
      fechaEstimadaLlegada: '2026-11-20T12:00:00Z' as string | null,
      observacion: 'Sale en camión por la mañana' as string | null,
    },
  ]
}

let ordenesCompraMock = crearOrdenesCompraMock()

/** Solo para tests: vuelve las órdenes de compra simuladas a su estado inicial. */
export function reiniciarOrdenesCompraMock() {
  ordenesCompraMock = crearOrdenesCompraMock()
  chatMensajesMock = {}
}

// Chat por orden simulado (#225): mensajes por orden, en memoria. No hay hub en modo simulado: el chat consulta cada 10 s.
interface MensajeChatMock {
  id: string
  ordenCompraId: string
  autorId: string | null
  autorNombre: string
  autorRol: string
  esSistema: boolean
  texto: string
  fechaCreacion: string
}
let chatMensajesMock: Record<string, MensajeChatMock[]> = {}

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
      // Fecha fija y lejana: con una cercana, el día que faltaban menos de 24 h pasaba a "por vencer", sumaba un aviso
      // a la campana y rompía tests que cuentan avisos. Con una relativa a hoy los tests no podrían verificar el texto.
      fechaLimiteCotizacion: '2099-12-15T18:00:00Z',
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

/** Un renglón cotizado. Los campos de la otra presentación son los del contrato de la #190. */
interface ItemCotizacionMock {
  articuloId: string
  precioUnitario: number
  presentacionOfrecida?: string | null
  cantidadOfrecida?: number | null
  subtotalItem?: number
  coberturaParcial?: boolean
}

interface CotizacionMock {
  cotizacionId: string
  licitacionId: string
  proveedorId: string
  flete?: number
  plazoEntregaDias?: number
  fechaCreacion: string
  items: ItemCotizacionMock[]
  puntajeTotal: number
  esGanadora: boolean
}

/**
 * Completa, como lo hace el servicio real (#190), el total de cada renglón y la marca de cobertura parcial.
 * Regla SOLO DEL MOCK: hay cobertura parcial si la cantidad ofrecida es menor que la del pliego.
 * El servicio real decide con su propia regla (equivalencias entre presentaciones, a definir con Licitaciones).
 */
function completarItemsCotizacionMock(
  items: ItemCotizacionMock[],
  pliego: Array<{ articuloId: string; cantidad: number }>,
): ItemCotizacionMock[] {
  return items.map((it) => {
    const pedido = pliego.find((p) => p.articuloId === it.articuloId)?.cantidad ?? 0
    const cantidad = it.cantidadOfrecida ?? pedido
    return { ...it, subtotalItem: cantidad * it.precioUnitario, coberturaParcial: cantidad < pedido }
  })
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
// Calificaciones de proveedores (1 a 5): una por usuario y proveedor, como el servicio. La calificación
// del proveedor es el promedio; un proveedor ausente está "sin calificar".
const calificacionesMock = new Map<string, Map<string, number>>()

/** Promedio (2 decimales) de las calificaciones de un proveedor, o null si no tiene. */
function promedioDeProveedor(proveedorId: string): number | null {
  const porUsuario = calificacionesMock.get(proveedorId)
  if (!porUsuario || porUsuario.size === 0) return null
  const valores = [...porUsuario.values()]
  return Math.round((valores.reduce((a, b) => a + b, 0) / valores.length) * 100) / 100
}

// Magic Links generados por el mock (#252): solo para que cada token salga distinto y sea fácil de reconocer.
let magicLinksGenerados = 0

/** Solo para tests: vuelve las licitaciones y cotizaciones simuladas a su estado inicial. */
export function reiniciarLicitacionesMock() {
  licitacionesMock = crearLicitacionesMock()
  cotizacionesMock = crearCotizacionesMock()
  calificacionesMock.clear()
  magicLinksGenerados = 0
}

// Paginación de los listados (#172). Imita a los servicios: ?pagina=&tamano= opcionales
// (por defecto 1 y 50, máximo 50), 400 si están fuera de rango y el total en X-Total-Count.
const TAMANO_MAXIMO_MOCK = 50

function responderPaginado<T>(request: Request, lista: T[]) {
  const url = new URL(request.url)
  const pagina = Number(url.searchParams.get('pagina') ?? '1')
  const tamano = Number(url.searchParams.get('tamano') ?? String(TAMANO_MAXIMO_MOCK))
  const valido =
    Number.isInteger(pagina) &&
    pagina >= 1 &&
    Number.isInteger(tamano) &&
    tamano >= 1 &&
    tamano <= TAMANO_MAXIMO_MOCK
  if (!valido) {
    return HttpResponse.json(
      {
        title: 'Paginación inválida',
        status: 400,
        detail: `La página debe ser 1 o mayor y el tamaño debe estar entre 1 y ${TAMANO_MAXIMO_MOCK}.`,
      },
      { status: 400 },
    )
  }
  const desde = (pagina - 1) * tamano
  return HttpResponse.json(lista.slice(desde, desde + tamano), {
    status: 200,
    headers: {
      'X-Total-Count': String(lista.length),
      'X-Page': String(pagina),
      'X-Page-Size': String(tamano),
    },
  })
}

export const handlers: RequestHandler[] = [
  // Contrato: POST /api/usuarios/tokens → 200 AuthResponseDto | 401 ProblemDetails
  // Se usa '*/api/usuarios/tokens' para interceptar la ruta tanto relativa (en browser con proxy)
  // como con origen explícito http://localhost:5000 (en Vitest).
  http.post('*/api/usuarios/tokens', async ({ request }) => {
    const body = (await request.json()) as { email?: string; password?: string }

    const usuario = USUARIOS_MOCK.find((u) => u.email === body.email)

    if (usuario && body.password === MOCK_PASSWORD) {
      return HttpResponse.json(
        {
          id: usuario.id,
          nombreCompleto: usuario.nombreCompleto,
          email: usuario.email,
          token: usuario.rol === 'Admin' ? MOCK_TOKEN_ADMIN
            : usuario.rol === 'Comprador' ? MOCK_TOKEN_COMPRADOR
              : MOCK_TOKEN_CAPATAZ,
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

  // Contrato: POST /api/usuarios/password/recuperaciones → 200 { mensaje } | 400 ProblemDetails.
  // Como el backend real, responde siempre lo mismo exista o no la cuenta; solo falla (400)
  // si el email viene vacío. Es público: no necesita sesión.
  http.post('*/api/usuarios/password/recuperaciones', async ({ request }) => {
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

  // Contrato: POST /api/usuarios/password/restablecimientos → 200 { mensaje } | 400 ProblemDetails.
  // Es público: no necesita sesión. Para ver el error del enlace vencido, abrir
  // /restablecer-contrasena?email=capataz%40obrasync.com&token=token-vencido
  http.post('*/api/usuarios/password/restablecimientos', async ({ request }) => {
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
    if (body.nuevaPassword.length < 8) {
      return HttpResponse.json(
        { title: 'Contraseña inválida', status: 400, detail: 'La contraseña debe tener al menos 8 caracteres.' },
        { status: 400 },
      )
    }
    return HttpResponse.json({ mensaje: 'Contraseña restablecida exitosamente.' }, { status: 200 })
  }),

  // Contrato: POST /api/usuarios/password/cambios → 200 { mensaje } | 400 | 401 ProblemDetails.
  // Requiere sesión (JWT). La contraseña actual de todos los usuarios simulados es MOCK_PASSWORD.
  // Para ver el 401 (sesión vencida) se puede mandar la contraseña actual "token-vencido".
  http.post('*/api/usuarios/password/cambios', async ({ request }) => {
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
    if (body.nuevaPassword.length < 8) {
      return invalido('La contraseña debe tener al menos 8 caracteres.')
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
  http.get('*/api/plantillas-obra', ({ request }) => responderPaginado(request, PLANTILLAS_MOCK)),

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
  http.get('*/api/usuarios/capataces', ({ request }) => {
    return responderPaginado(
      request,
      USUARIOS_MOCK.filter((u) => u.rol === 'Capataz').map((u) => ({
        id: u.id,
        nombreCompleto: u.nombreCompleto,
      })),
    )
  }),

  // Contrato: GET /api/usuarios/proveedores → 200 [{ id, nombreCompleto }] (Comprador, Admin; #157)
  http.get('*/api/usuarios/proveedores', ({ request }) => {
    return responderPaginado(
      request,
      USUARIOS_MOCK.filter((u) => u.rol === 'Proveedor').map((u) => ({
        id: u.id,
        nombreCompleto: u.nombreCompleto,
      })),
    )
  }),

  // Contrato: POST /api/usuarios → 201 AuthResponseDto | 400 | 403 | 409 (email repetido).
  // Agrega el usuario a la lista del mock, así el capataz nuevo aparece en GET /capataces.
  http.post('*/api/usuarios', async ({ request }) => {
    const body = (await request.json()) as {
      nombreCompleto?: string
      email?: string
      password?: string
      rol?: string
    }
    if (!body.nombreCompleto || !body.email || !body.password || body.password.length < 8 || !/\d/.test(body.password)) {
      return HttpResponse.json(
        { title: 'Datos inválidos', status: 400, detail: 'Los datos del usuario son inválidos.' },
        { status: 400 },
      )
    }
    if (body.rol !== 'Proveedor' && body.rol !== 'Capataz') {
      return HttpResponse.json(
        { title: 'Datos inválidos', status: 400, detail: 'El rol indicado no es válido.' },
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
      { id: nuevo.id, nombreCompleto: nuevo.nombreCompleto, email: nuevo.email, token: nuevo.rol === 'Proveedor' ? MOCK_TOKEN_PROVEEDOR : MOCK_TOKEN_CAPATAZ, roles: [nuevo.rol] },
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
    return responderPaginado(request, resultado)
  }),

  // Contrato: POST /api/catalogo/insumos → 201 InsumoCatalogoDto (Comprador, Admin) (#215).
  // 400 datos inválidos, 401 sin sesión, 403 rol sin permiso, 409 código repetido.
  // No agrega el insumo a MOCK_CATALOGO: así las otras pantallas y pruebas ven siempre el mismo catálogo.
  http.post('*/api/catalogo/insumos', async ({ request }) => {
    const rol = rolDelJwt(request)
    if (!rol) {
      return HttpResponse.json(
        { title: 'No autenticado', status: 401, detail: 'Se requiere un token JWT válido.' },
        { status: 401 },
      )
    }
    if (rol !== 'Comprador' && rol !== 'Admin') {
      return HttpResponse.json(
        { title: 'Prohibido', status: 403, detail: 'Solo Comprador o Admin pueden cargar insumos.' },
        { status: 403 },
      )
    }
    const cuerpo = (await request.json().catch(() => null)) as {
      codigo?: string
      descripcion?: string
      unidadMedida?: string
      moneda?: string
      precioReferencia?: number | null
      activo?: boolean
    } | null
    if (
      !cuerpo ||
      !cuerpo.codigo ||
      !cuerpo.descripcion ||
      !cuerpo.unidadMedida ||
      (cuerpo.moneda !== undefined && cuerpo.moneda !== 'ARS') ||
      (typeof cuerpo.precioReferencia === 'number' && cuerpo.precioReferencia < 0)
    ) {
      return HttpResponse.json(
        { title: 'Solicitud inválida', status: 400, detail: 'Datos de insumo inválidos o divisa no permitida.' },
        { status: 400 },
      )
    }
    const codigo = cuerpo.codigo
    if (MOCK_CATALOGO.some((i) => i.codigo.toLowerCase() === codigo.toLowerCase())) {
      return HttpResponse.json(
        { title: 'Conflicto', status: 409, detail: `Ya existe un insumo con el código ${codigo}.` },
        { status: 409 },
      )
    }
    return HttpResponse.json(
      {
        articuloId: crypto.randomUUID(),
        codigo,
        descripcion: cuerpo.descripcion,
        unidadMedida: cuerpo.unidadMedida,
        moneda: cuerpo.moneda ?? 'ARS',
        precioReferencia: cuerpo.precioReferencia ?? null,
        activo: cuerpo.activo ?? true,
      },
      { status: 201 },
    )
  }),

  // Contrato: GET /api/obras → 200 Obra[] (Admin, Comprador, Capataz)
  http.get('*/api/obras', ({ request }) => {
    return responderPaginado(request, obrasMock)
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

  // Contrato: POST /api/asistente/chat → 200 ChatResponseDto | 400 | 429 | 503 (asistente-openapi.json, #138)
  // Asistente SIMULADO: no hay modelo de IA. Busca una cantidad y un material del catálogo en el mensaje;
  // si encuentra los dos arma un BORRADOR de requisición (sin crear nada), si no pide el dato que falta.
  // Los casos de 429, 503 y red caída se configuran en los tests con server.use().
  http.post('*/api/asistente/chat', async ({ request }) => {
    const body = (await request.json()) as { mensaje?: string }
    const mensaje = (body.mensaje ?? '').trim()
    if (!mensaje || mensaje.length > 500) {
      return HttpResponse.json(
        { title: 'Mensaje inválido', status: 400, detail: 'El mensaje debe tener entre 1 y 500 caracteres.' },
        { status: 400 },
      )
    }

    const texto = mensaje.toLowerCase()

    // Perfil Capataz (#147): consulta de sus requisiciones, solo lectura (no arma ninguna tarjeta).
    if (/mis requisiciones/.test(texto)) {
      const masNueva = requisicionesMock[0]
      const respuesta = masNueva
        ? `Tenés ${requisicionesMock.length} requisición(es). La más nueva es la ${masNueva.numeroRequisicion} y está ${masNueva.estado.toLowerCase()}.`
        : 'Todavía no tenés requisiciones.'
      return HttpResponse.json({ respuesta, borrador: null, tokensUsados: 0 }, { status: 200 })
    }

    // Perfil Capataz (#147): registrar lo que llegó. Solo ARMA el borrador de una recepción TOTAL; la recepción
    // real la hace el frontend con POST /api/recepciones cuando el Capataz toca Confirmar. El remito lo da la
    // persona: si no lo dijo, se lo pide (nunca se inventa).
    if (/lleg|recib|remito/.test(texto)) {
      const orden = ordenesPendientesMock[0]
      if (!orden) {
        return HttpResponse.json(
          { respuesta: 'No hay órdenes pendientes de recepción por ahora.', borrador: null, tokensUsados: 0 },
          { status: 200 },
        )
      }
      const remito = mensaje.match(/remito\s+(?:n[°º.]?\s*)?([\w-]+)/i)?.[1]
      if (!remito) {
        return HttpResponse.json(
          { respuesta: 'Dale. ¿Cuál es el número de remito?', borrador: null, tokensUsados: 0 },
          { status: 200 },
        )
      }
      const obraDeLaOrden = obrasMock.find((o) => o.id === orden.obraId)
      return HttpResponse.json(
        {
          respuesta: `Armé la recepción del remito ${remito}. Revisala y tocá Confirmar para registrarla.`,
          borrador: null,
          recepcion: {
            accion: 'registrarRecepcion',
            titulo: `Registrar recepción · remito ${remito}`,
            resumen: {
              obra: obraDeLaOrden?.nombre ?? null,
              etapa: null,
              numeroRemito: remito,
              items: orden.items.map((i) => ({
                descripcion: i.descripcionArticulo,
                cantidad: i.cantidadEsperada,
                unidadMedida: i.unidadMedida,
              })),
            },
            recepcion: {
              obraId: orden.obraId,
              ordenCompraId: orden.ordenCompraId,
              numeroRemito: remito,
              observaciones: null,
              detalles: orden.items.map((i) => ({
                articuloId: i.articuloId,
                descripcionArticulo: i.descripcionArticulo,
                cantidadEsperada: i.cantidadEsperada,
                cantidadRecibida: i.cantidadEsperada,
                unidadMedida: i.unidadMedida,
                conforme: true,
              })),
            },
          },
          tokensUsados: 0,
        },
        { status: 200 },
      )
    }

    // Perfil Comprador (#141): consulta y aprobación. Aprobar solo ARMA el borrador; la aprobación real
    // la hace el frontend con POST /api/requisiciones/{id}/aprobaciones cuando el Comprador toca Confirmar.
    if (/aprob|pendiente/.test(texto)) {
      const pendientes = requisicionesMock.filter((r) => r.estado === 'Pendiente')
      const primera = pendientes[0]
      if (!primera) {
        return HttpResponse.json(
          { respuesta: 'No hay requisiciones pendientes por ahora.', borrador: null, tokensUsados: 0 },
          { status: 200 },
        )
      }
      if (!/aprob/.test(texto)) {
        return HttpResponse.json(
          {
            respuesta: `Hay ${pendientes.length} requisición(es) pendiente(s). La más nueva es la ${primera.numeroRequisicion}.`,
            borrador: null,
            tokensUsados: 0,
          },
          { status: 200 },
        )
      }
      const obraDeLaRequisicion = obrasMock.find((o) => o.id === primera.obraId)
      return HttpResponse.json(
        {
          respuesta: `Armé la aprobación de la ${primera.numeroRequisicion}. Revisala y tocá Aprobar para confirmar.`,
          borrador: null,
          aprobacion: {
            accion: 'aprobarRequisicion',
            titulo: `Aprobar requisición ${primera.numeroRequisicion}`,
            requisicionId: primera.requisicionId,
            resumen: {
              numero: primera.numeroRequisicion,
              obra: obraDeLaRequisicion?.nombre ?? null,
              etapa: primera.etapa ?? null,
              items: primera.items.map((i) => ({
                descripcion: i.descripcion,
                cantidad: i.cantidad,
                unidadMedida: i.unidadMedida,
              })),
            },
          },
          tokensUsados: 0,
        },
        { status: 200 },
      )
    }

    const cantidadTexto = texto.match(/(\d+(?:[.,]\d+)?)/)?.[1]
    const cantidad = cantidadTexto ? Number(cantidadTexto.replace(',', '.')) : 0
    const insumo = [
      { palabra: 'cemento', indice: 0 },
      { palabra: 'arena', indice: 1 },
      { palabra: 'cal', indice: 2 },
    ]
      .filter((m) => new RegExp(`\\b${m.palabra}\\b`).test(texto))
      .map((m) => MOCK_CATALOGO[m.indice])[0]
    const obra = obrasMock[0]

    if (!insumo || cantidad <= 0 || !obra) {
      const pideMateriales = texto.includes('pedir materiales') && !insumo && cantidad <= 0
      return HttpResponse.json(
        {
          respuesta: pideMateriales
            ? 'Dale. ¿Qué material necesitás y en qué cantidad?'
            : 'Para armar la requisición necesito el material y la cantidad. Por ejemplo: "necesito 20 bolsas de cemento".',
          borrador: null,
          tokensUsados: 0,
        },
        { status: 200 },
      )
    }

    const etapa = obra.etapas.find((e) => e.estado !== 'Completada')?.nombreEtapa ?? null
    return HttpResponse.json(
      {
        respuesta: `Armé un borrador con ${cantidad} ${insumo.unidadMedida.toLowerCase()} de ${insumo.descripcion} para ${obra.nombre}. Revisalo y tocá Confirmar para crearlo.`,
        borrador: {
          accion: 'crearRequisicion',
          titulo: 'Nueva requisición',
          resumen: {
            obra: obra.nombre,
            etapa,
            items: [{ descripcion: insumo.descripcion, cantidad, unidadMedida: insumo.unidadMedida }],
          },
          requisicion: {
            obraId: obra.id,
            etapa,
            observaciones: 'Armada con el asistente',
            items: [{ articuloId: insumo.articuloId, cantidad }],
          },
        },
        tokensUsados: 0,
      },
      { status: 200 },
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
    return responderPaginado(request, lista)
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

  // Contrato: PUT /api/requisiciones/{id} â†’ 200 | 403 | 404 | 409 ProblemDetails
  http.put('*/api/requisiciones/:id', async ({ params, request }) => {
    const req = requisicionesMock.find((r) => r.requisicionId === params.id)
    if (!req) return HttpResponse.json({ title: 'No encontrada', status: 404 }, { status: 404 })
    if (req.solicitanteId !== 'a1b2c3d4-0000-0000-0000-000000000001') {
      return HttpResponse.json({ title: 'Acceso denegado', status: 403 }, { status: 403 })
    }
    if (req.estado !== 'Rechazada' && req.estado !== 'Pendiente') {
      return HttpResponse.json({ title: 'Conflicto de estado', status: 409 }, { status: 409 })
    }
    const body = await request.json() as { etapa?: string | null; observaciones?: string | null; items: { articuloId: string; cantidad: number }[] }
    if (!Array.isArray(body.items) || body.items.length === 0 || body.items.some((item) => !item.articuloId || !Number.isFinite(item.cantidad) || item.cantidad <= 0)) {
      return HttpResponse.json({ title: 'Datos inválidos', status: 400, detail: 'Los ítems y cantidades deben ser válidos.' }, { status: 400 })
    }
    req.etapa = body.etapa ?? req.etapa
    req.observaciones = body.observaciones ?? null
    req.items = body.items.map((item) => {
      const insumo = MOCK_CATALOGO.find((catalogo) => catalogo.articuloId === item.articuloId)
      return { articuloId: item.articuloId, descripcion: insumo?.descripcion ?? item.articuloId, cantidad: item.cantidad, unidadMedida: insumo?.unidadMedida ?? '' }
    })
    req.estado = 'Pendiente'
    req.motivoRechazo = null
    req.fechaRechazo = null
    return HttpResponse.json(req, { status: 200 })
  }),

  // Contrato: GET /api/licitaciones/seguimiento/{requisicionId} → 200 SeguimientoRequisicionDto
  // Para la requisición aprobada de ejemplo (0009) simula una licitación abierta con 2 ofertas; el resto, sin licitación.
  http.get('*/api/licitaciones/seguimiento/:requisicionId', ({ params }) => {
    const requisicionId = String(params.requisicionId)
    if (requisicionId === 'd1d2d3d4-0000-0000-0000-000000000009') {
      return HttpResponse.json({
        requisicionId,
        licitacionId: 'e1e2e3e4-0000-0000-0000-000000000001',
        estadoLicitacion: 'Abierta',
        fechaLimiteCotizacion: new Date(Date.now() + 3 * 24 * 3600 * 1000).toISOString(),
        cantidadCotizaciones: 2,
        ordenesCompra: [],
      })
    }
    return HttpResponse.json({ requisicionId, licitacionId: null, estadoLicitacion: null, fechaLimiteCotizacion: null, cantidadCotizaciones: 0, ordenesCompra: [] })
  }),

  // Contrato: POST /api/requisiciones/{id}/cancelaciones → 200 | 403 | 404 | 409 ProblemDetails
  http.post('*/api/requisiciones/:id/cancelaciones', ({ params }) => {
    const req = requisicionesMock.find((r) => r.requisicionId === params.id)
    if (!req) return HttpResponse.json({ title: 'No encontrada', status: 404 }, { status: 404 })
    if (req.solicitanteId !== 'a1b2c3d4-0000-0000-0000-000000000001') {
      return HttpResponse.json({ title: 'Acceso denegado', status: 403 }, { status: 403 })
    }
    if (req.estado !== 'Pendiente') {
      return HttpResponse.json(
        { title: 'Conflicto', status: 409, detail: 'Solo se pueden cancelar requisiciones pendientes.' },
        { status: 409 },
      )
    }
    req.estado = 'Cancelada'
    return HttpResponse.json(req, { status: 200 })
  }),

  // Contrato: POST /api/requisiciones/{id}/aprobaciones → 200 | 404 | 409 ProblemDetails
  http.post('*/api/requisiciones/:id/aprobaciones', ({ params }) => {
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

  // Contrato: POST /api/requisiciones/{id}/rechazos → 200 | 400 | 404 | 409 ProblemDetails
  http.post('*/api/requisiciones/:id/rechazos', async ({ params, request }) => {
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

  // Contrato (#218): GET /api/ordenes-compra/chat/no-leidos → 200 ResumenNoLeidosChatDto (solo las órdenes con no leídos)
  http.get('*/api/ordenes-compra/chat/no-leidos', () =>
    HttpResponse.json({ totalNoLeidos: 0, ordenes: [] }, { status: 200 }),
  ),

  // Contrato (#218): GET /api/ordenes-compra/{id}/chat/mensajes → 200 MensajeChatOrdenDto[] (con X-Total-Count)
  http.get('*/api/ordenes-compra/:id/chat/mensajes', ({ params, request }) => {
    const id = params.id as string
    if (!ordenesCompraMock.some((o) => o.id === id)) {
      return HttpResponse.json(
        { title: 'Recurso no encontrado', status: 404, detail: `No se encontró la orden de compra con ID ${id}.` },
        { status: 404 },
      )
    }
    return responderPaginado(request, chatMensajesMock[id] ?? [])
  }),

  // Contrato (#218): POST /api/ordenes-compra/{id}/chat/mensajes { texto } → 201 MensajeChatOrdenDto | 400 | 404
  http.post('*/api/ordenes-compra/:id/chat/mensajes', async ({ params, request }) => {
    const id = params.id as string
    if (!ordenesCompraMock.some((o) => o.id === id)) {
      return HttpResponse.json(
        { title: 'Recurso no encontrado', status: 404, detail: `No se encontró la orden de compra con ID ${id}.` },
        { status: 404 },
      )
    }
    const texto = await request.text()
    const cuerpo = (texto ? JSON.parse(texto) : {}) as { texto?: string }
    const mensaje = (cuerpo.texto ?? '').trim()
    if (mensaje.length === 0 || mensaje.length > 1000) {
      return HttpResponse.json(
        { title: 'Solicitud inválida', status: 400, detail: 'El mensaje debe tener entre 1 y 1000 caracteres.' },
        { status: 400 },
      )
    }
    const rol = rolDelJwt(request) ?? 'Comprador'
    const usuario = USUARIOS_MOCK.find((u) => u.rol === rol) ?? USUARIOS_MOCK[1]
    const nuevo: MensajeChatMock = {
      id: crypto.randomUUID(),
      ordenCompraId: id,
      autorId: usuario.id,
      autorNombre: usuario.nombreCompleto,
      autorRol: usuario.rol,
      esSistema: false,
      texto: mensaje,
      fechaCreacion: new Date().toISOString(),
    }
    chatMensajesMock[id] = [...(chatMensajesMock[id] ?? []), nuevo]
    return HttpResponse.json(nuevo, { status: 201 })
  }),

  // Contrato (#218): POST /api/ordenes-compra/{id}/chat/lecturas → 200 LecturaChatResponseDto (idempotente)
  http.post('*/api/ordenes-compra/:id/chat/lecturas', ({ params, request }) => {
    const rol = rolDelJwt(request) ?? 'Comprador'
    const usuario = USUARIOS_MOCK.find((u) => u.rol === rol) ?? USUARIOS_MOCK[1]
    return HttpResponse.json(
      { ordenCompraId: params.id as string, usuarioId: usuario.id, ultimaLecturaEn: new Date().toISOString() },
      { status: 200 },
    )
  }),

  // Contrato (#218): POST /api/ordenes-compra/{id}/problema/resoluciones (solo Comprador/Admin) → 200 ProblemaOrdenEstadoDto | 403 | 404
  http.post('*/api/ordenes-compra/:id/problema/resoluciones', ({ params, request }) => {
    const rol = rolDelJwt(request)
    if (rol !== 'Comprador' && rol !== 'Admin') {
      return HttpResponse.json(
        { title: 'Acceso denegado', status: 403, detail: 'Solo Compras puede marcar el problema como resuelto.' },
        { status: 403 },
      )
    }
    return HttpResponse.json(
      {
        ordenCompraId: params.id as string,
        tieneProblemaAbierto: false,
        motivoProblemaAbierto: null,
        problemaResueltoEn: new Date().toISOString(),
      },
      { status: 200 },
    )
  }),

  // Contrato: GET /api/ordenes-compra → 200 OrdenCompraListadoDto[] (con X-Total-Count)
  http.get('*/api/ordenes-compra', ({ request }) => responderPaginado(request, ordenesCompraMock)),

  // Contrato: POST /api/ordenes-compra/{id}/despachos (solo el Proveedor dueño, #216)
  //   → 200 OrdenCompraListadoDto | 400 | 403 | 404 ProblemDetails. Idempotente: si ya estaba despachada, responde 200.
  http.post('*/api/ordenes-compra/:id/despachos', async ({ params, request }) => {
    const id = params.id as string
    const orden = ordenesCompraMock.find((o) => o.id === id)
    if (!orden) {
      return HttpResponse.json(
        { title: 'Recurso no encontrado', status: 404, detail: `No se encontró la orden de compra con ID ${id}.` },
        { status: 404 },
      )
    }
    // Re-despacho del remanente (#248): solo si la recepción fue parcial; si ya está en camino o completa, 409.
    const esRedespacho = orden.fueDespachada && orden.puedeRedespachar === true
    if (orden.fueDespachada && !esRedespacho) {
      return HttpResponse.json(
        { title: 'Conflicto', status: 409, detail: 'La orden ya está en camino o ya se recibió completa.' },
        { status: 409 },
      )
    }

    const texto = await request.text()
    const cuerpo = (texto ? JSON.parse(texto) : {}) as { fechaEstimadaLlegada?: string; observacion?: string }
    // El backend compara la fecha en UTC.
    const hoyUtc = new Date().toISOString().slice(0, 10)
    if (cuerpo.fechaEstimadaLlegada && cuerpo.fechaEstimadaLlegada.slice(0, 10) < hoyUtc) {
      return HttpResponse.json(
        { title: 'Solicitud inválida', status: 400, detail: 'La fecha estimada de llegada no puede ser anterior a hoy.' },
        { status: 400 },
      )
    }
    if ((cuerpo.observacion ?? '').trim().length > 200) {
      return HttpResponse.json(
        { title: 'Solicitud inválida', status: 400, detail: 'La observación no puede superar los 200 caracteres.' },
        { status: 400 },
      )
    }
    orden.estado = 'Despachada'
    orden.fueDespachada = true
    orden.numeroDespacho = (orden.numeroDespacho ?? 0) + 1
    orden.puedeRedespachar = false
    orden.tieneRecepcionParcialPendiente = false
    orden.fechaDespacho = new Date().toISOString()
    orden.fechaEstimadaLlegada = cuerpo.fechaEstimadaLlegada ?? null
    orden.observacion = cuerpo.observacion?.trim() || null
    return HttpResponse.json(orden, { status: 200 })
  }),

  // Contrato: GET /api/recepciones/ordenes-pendientes?obraId= → 200 OrdenPendienteDto[]
  http.get('*/api/recepciones/ordenes-pendientes', ({ request }) => {
    const obraId = new URL(request.url).searchParams.get('obraId')
    const lista = ordenesPendientesMock.filter((o) => !obraId || o.obraId === obraId)
    return responderPaginado(request, lista)
  }),

  http.get('*/api/recepciones', ({ request }) => {
    const url = new URL(request.url)
    const ordenCompraId = url.searchParams.get('ordenCompraId')
    const lista = ordenCompraId
      ? recepcionesMock[ordenCompraId] ?? []
      : Object.values(recepcionesMock).flat()
    return responderPaginado(request, lista)
  }),

  // Contrato: POST /api/asistente/remitos/lecturas (multipart, campo "imagen") → 200 LecturaRemitoDto | 400 | 429 | 503
  // Lectura SIMULADA: no hay modelo de IA. Devuelve siempre el mismo remito de ayer; los casos de "no se pudo leer",
  // 429 y 503 se configuran en los tests con server.use().
  http.post('*/api/asistente/remitos/lecturas', async ({ request }) => {
    if (!(request.headers.get('content-type') ?? '').startsWith('multipart/form-data')) {
      return HttpResponse.json(
        { title: 'Pedido inválido', status: 400, detail: 'Falta la imagen del remito (campo \'imagen\').' },
        { status: 400 },
      )
    }
    const ayer = new Date()
    ayer.setDate(ayer.getDate() - 1)
    return HttpResponse.json({ numeroRemito: 'R-0001-00012345', fecha: hoyComoTexto(ayer) }, { status: 200 })
  }),

  // Contrato: POST /api/recepciones → 201 RecepcionDto | 400 | 409 ProblemDetails
  // Simula recepciones parciales y totales con saldo acumulado e historial.
  http.post('*/api/recepciones', async ({ request }) => {
    const body = (await request.json()) as {
      obraId?: string
      ordenCompraId?: string
      numeroRemito?: string
      fechaRecepcion?: string
      observaciones?: string | null
      detalles?: { articuloId: string; descripcionArticulo: string; cantidadEsperada: number; cantidadRecibida: number; unidadMedida: string; conforme: boolean }[]
    }
    if (body.ordenCompraId && ordenesCompletadasMock.has(body.ordenCompraId)) {
      return HttpResponse.json(
        { title: 'Conflicto', status: 409, detail: 'La orden de compra ya fue recibida por completo.' },
        { status: 409 },
      )
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
    if ((recepcionesMock[orden.ordenCompraId] ?? []).some((r) => r.numeroRemito === body.numeroRemito?.trim())) return HttpResponse.json({ title: 'Conflicto', status: 409, detail: 'El número de remito ya se usó en esta orden.' }, { status: 409 })
    if (body.detalles.some((d) => !Number.isFinite(d.cantidadRecibida) || d.cantidadRecibida < 0)) {
      return HttpResponse.json(
        { title: 'Cantidad inválida', status: 400, detail: 'La cantidad recibida no puede ser negativa.' },
        { status: 400 },
      )
    }
    if (!body.detalles.some((d) => d.cantidadRecibida > 0)) return HttpResponse.json({ title: 'Cantidad inválida', status: 400, detail: 'Al menos un artículo debe tener cantidad recibida mayor que cero.' }, { status: 400 })
    const articulosEnviados = new Set(body.detalles.map((detalle) => detalle.articuloId))
    if (articulosEnviados.size !== body.detalles.length || articulosEnviados.size !== orden.items.length || orden.items.some((item) => !articulosEnviados.has(item.articuloId))) {
      return HttpResponse.json({ title: 'Datos inválidos', status: 400, detail: 'La recepción debe incluir todos los artículos de la orden, una sola vez cada uno.' }, { status: 400 })
    }
    const detalleValidado = body.detalles.map((detalle) => {
      const item = orden.items.find((i) => i.articuloId === detalle.articuloId)
      return { detalle, item }
    })
    if (detalleValidado.some(({ detalle, item }) => !item || detalle.cantidadEsperada !== item.cantidadEsperada || detalle.cantidadRecibida > item.cantidadPendiente)) return HttpResponse.json({ title: 'Cantidad inválida', status: 400, detail: 'La cantidad recibida supera el saldo pendiente o el detalle no coincide con la orden.' }, { status: 400 })
    const cantidadesPorArticulo = new Map(body.detalles.map((detalle) => [detalle.articuloId, detalle.cantidadRecibida]))
    const completos = orden.items.every((item) => item.cantidadRecibida + (cantidadesPorArticulo.get(item.articuloId) ?? 0) >= item.cantidadEsperada)
    const idRecepcion = crypto.randomUUID()
    const creada: RecepcionHistorial = {
        id: idRecepcion, obraId: orden.obraId, ordenCompraId: orden.ordenCompraId,
        recepcionadoPorUsuarioId: 'a1b2c3d4-0000-0000-0000-000000000001',
        numeroRemito: body.numeroRemito.trim(), fechaRecepcion: body.fechaRecepcion,
        estado: completos ? 3 : 2, observaciones: body.observaciones ?? null,
        createdAt: new Date().toISOString(), updatedAt: null,
        detalles: body.detalles.map((detalle) => ({
          ...detalle,
          id: crypto.randomUUID(),
          recepcionId: idRecepcion,
          createdAt: new Date().toISOString(),
          updatedAt: null,
        })),
      }
    recepcionesMock[orden.ordenCompraId] = [...(recepcionesMock[orden.ordenCompraId] ?? []), creada]
    // Una recepción válida posterior supera el problema abierto (#217).
    orden.tieneProblemaAbierto = false
    orden.items = orden.items.map((item) => {
      const cantidadRecibida = cantidadesPorArticulo.get(item.articuloId) ?? 0
      return { ...item, cantidadRecibida: item.cantidadRecibida + cantidadRecibida, cantidadPendiente: item.cantidadPendiente - cantidadRecibida }
    })
    if (completos) {
      ordenesCompletadasMock.add(orden.ordenCompraId)
      ordenesCompletasMock.set(orden.ordenCompraId, orden)
      ordenesPendientesMock = ordenesPendientesMock.filter((o) => o.ordenCompraId !== orden.ordenCompraId)
    }
    return HttpResponse.json(
      creada,
      { status: 201 },
    )
  }),

  // Contrato: POST /api/recepciones/problemas (multipart) → 201 RecepcionDto (estado 4 = Rechazada) | 400 | 409
  // Registra una entrega con problema (#217): NO cambia lo recibido ni el saldo, la orden sigue pendiente.
  http.post('*/api/recepciones/problemas', async ({ request }) => {
    const respuesta = (status: number, title: string, detail: string) =>
      HttpResponse.json({ title, status, detail }, { status })
    let formulario: FormData
    try {
      formulario = await request.formData()
    } catch {
      return respuesta(400, 'Pedido inválido', 'El pedido tiene que ser multipart/form-data.')
    }
    const texto = (campo: string) => {
      const valor = formulario.get(campo)
      return typeof valor === 'string' ? valor : ''
    }
    const ordenCompraId = texto('ordenCompraId')
    const motivo = texto('motivo')
    const detalle = texto('detalle')
    if (!(MOTIVOS_PROBLEMA as readonly string[]).includes(motivo)) {
      return respuesta(400, 'Motivo inválido', 'Elegí uno de los motivos de la lista.')
    }
    if (detalle.length > 500) return respuesta(400, 'Detalle inválido', 'El detalle no puede superar los 500 caracteres.')
    const archivos = formulario.getAll('fotos').filter((valor) => typeof valor !== 'string')
    if (archivos.length > 3) return respuesta(400, 'Demasiadas fotos', 'Se permiten como máximo 3 fotos por entrega con problema.')
    for (const archivo of archivos) {
      if (archivo.size === 0) return respuesta(400, 'Foto inválida', 'La foto de evidencia está vacía.')
      if (archivo.size > 2 * 1024 * 1024) return respuesta(400, 'Foto inválida', 'Cada foto de evidencia no puede superar los 2 MB.')
      if (!['image/jpeg', 'image/png', 'image/webp'].includes(archivo.type)) {
        return respuesta(400, 'Foto inválida', 'Las fotos tienen que ser JPEG, PNG o WebP.')
      }
    }
    if (ordenesCompletadasMock.has(ordenCompraId)) {
      return respuesta(409, 'Conflicto', 'La orden de compra ya fue recibida por completo.')
    }
    const orden = ordenesPendientesMock.find((o) => o.ordenCompraId === ordenCompraId)
    if (!orden) return respuesta(400, 'Orden inexistente', 'La orden de compra no existe o ya fue recibida.')

    const idRecepcion = crypto.randomUUID()
    const fotos = []
    for (const [indice, archivo] of archivos.entries()) {
      const fotoId = crypto.randomUUID()
      fotosEvidenciaMock.set(`${idRecepcion}/${fotoId}`, { contentType: archivo.type, bytes: await archivo.arrayBuffer() })
      fotos.push({
        id: fotoId,
        nombreArchivo: archivo.name || `foto-${indice + 1}.jpg`,
        contentType: archivo.type as 'image/jpeg' | 'image/png' | 'image/webp',
        tamanoBytes: archivo.size,
        orden: indice + 1,
        url: `/api/recepciones/${idRecepcion}/fotos/${fotoId}`,
      })
    }
    const creada: RecepcionHistorial = {
      id: idRecepcion,
      obraId: orden.obraId,
      ordenCompraId: orden.ordenCompraId,
      recepcionadoPorUsuarioId: 'a1b2c3d4-0000-0000-0000-000000000001',
      // Sin número de remito el servicio genera un identificador interno.
      numeroRemito: texto('numeroRemito').trim() || `PROBLEMA-${idRecepcion.slice(0, 8)}`,
      fechaRecepcion: new Date().toISOString(),
      estado: 4,
      motivoProblema: motivo as (typeof MOTIVOS_PROBLEMA)[number],
      observaciones: detalle.trim() || null,
      createdAt: new Date().toISOString(),
      updatedAt: null,
      // Una entrega con problema no suma nada: cada material queda en 0 y no conforme.
      detalles: orden.items.map((item) => ({
        id: crypto.randomUUID(),
        recepcionId: idRecepcion,
        articuloId: item.articuloId,
        descripcionArticulo: item.descripcionArticulo,
        cantidadEsperada: item.cantidadEsperada,
        cantidadRecibida: 0,
        unidadMedida: item.unidadMedida,
        conforme: false,
        observacion: null,
        createdAt: new Date().toISOString(),
        updatedAt: null,
      })),
      fotos,
    }
    recepcionesMock[orden.ordenCompraId] = [...(recepcionesMock[orden.ordenCompraId] ?? []), creada]
    orden.tieneProblemaAbierto = true
    return HttpResponse.json(creada, { status: 201 })
  }),

  // Contrato: GET /api/recepciones/ordenes/{ordenCompraId} → 200 RecepcionesDeOrdenDto | 404
  http.get('*/api/recepciones/ordenes/:ordenCompraId', ({ params }) => {
    const id = String(params.ordenCompraId)
    const orden = ordenesPendientesMock.find((o) => o.ordenCompraId === id) ?? ordenesCompletasMock.get(id)
    if (!orden) {
      // Las órdenes de Compras y del Proveedor (c1c2…) son otras que las de Recepción (e1e2…): para la demo, la ya
      // despachada responde con su despacho y sin movimientos todavía.
      const deCompras = ordenesCompraMock.find((o) => o.id === id && o.fueDespachada)
      if (deCompras) {
        return HttpResponse.json({
          ordenCompraId: deCompras.id,
          obraId: deCompras.obraId,
          etapa: null,
          fechaDespacho: deCompras.fechaDespacho,
          fechaEstimadaLlegada: deCompras.fechaEstimadaLlegada,
          observacionDespacho: deCompras.observacion,
          estaCompleta: false,
          tieneProblemaAbierto: false,
          items: [],
          recepciones: [],
        })
      }
      return HttpResponse.json({ title: 'Orden inexistente', status: 404, detail: 'La orden de compra no existe.' }, { status: 404 })
    }
    return HttpResponse.json({
      ordenCompraId: orden.ordenCompraId,
      obraId: orden.obraId,
      etapa: null,
      fechaDespacho: orden.fechaDespacho,
      fechaEstimadaLlegada: orden.fechaEstimadaLlegada,
      observacionDespacho: orden.observacionDespacho,
      estaCompleta: ordenesCompletadasMock.has(id),
      tieneProblemaAbierto: orden.tieneProblemaAbierto,
      items: orden.items,
      recepciones: recepcionesMock[id] ?? [],
    })
  }),

  // Contrato: GET /api/recepciones/{id}/fotos/{fotoId} → 200 (imagen) | 404
  http.get('*/api/recepciones/:id/fotos/:fotoId', ({ params }) => {
    const foto = fotosEvidenciaMock.get(`${String(params.id)}/${String(params.fotoId)}`)
    if (!foto) {
      return HttpResponse.json({ title: 'Foto inexistente', status: 404, detail: 'La foto no existe.' }, { status: 404 })
    }
    return new HttpResponse(foto.bytes, { status: 200, headers: { 'Content-Type': foto.contentType, 'Cache-Control': 'private' } })
  }),

  // Contrato: GET /api/licitaciones → 200 LicitacionResponseDto[]
  // Al Proveedor el backend ya le devuelve únicamente las abiertas.
  // yaCotizada: el backend real solo lo completa para el Proveedor (con el 'sub' de su token). El mock
  // no lee el token, así que lo calcula siempre para el proveedor de prueba.
  http.get('*/api/licitaciones', ({ request }) => {
    const proveedorId = USUARIOS_MOCK.find((u) => u.rol === 'Proveedor')?.id
    const abiertas = licitacionesMock
      .filter((l) => l.estado === 'Abierta')
      .map((l) => ({
        ...l,
        yaCotizada: cotizacionesMock.some(
          (c) => c.licitacionId === l.licitacionId && c.proveedorId === proveedorId,
        ),
      }))
    return responderPaginado(request, abiertas)
  }),

  // Contrato: POST /api/licitaciones → 201 LicitacionResponseDto | 400 | 404 | 409 ProblemDetails
  http.post('*/api/licitaciones', async ({ request }) => {
    const body = (await request.json().catch(() => ({}))) as {
      requisicionId?: string
      fechaLimiteCotizacion?: string
    }
    const req = requisicionesMock.find((r) => r.requisicionId === body.requisicionId)
    if (!req || req.estado !== 'Aprobada') {
      return HttpResponse.json(
        { title: 'Recurso no encontrado', status: 404, detail: 'La requisición no existe o no está aprobada.' },
        { status: 404 },
      )
    }
    const limite = new Date(body.fechaLimiteCotizacion ?? '')
    if (Number.isNaN(limite.getTime()) || limite.getTime() <= Date.now()) {
      return HttpResponse.json(
        { title: 'Solicitud inválida', status: 400, detail: 'La fecha límite tiene que ser futura.' },
        { status: 400 },
      )
    }
    if (licitacionesMock.some((l) => l.requisicionId === req.requisicionId && l.estado === 'Abierta')) {
      return HttpResponse.json(
        { title: 'Conflicto', status: 409, detail: 'Ya hay una licitación abierta para esa requisición.' },
        { status: 409 },
      )
    }
    const nueva = {
      licitacionId: crypto.randomUUID(),
      requisicionId: req.requisicionId,
      numeroRequisicion: req.numeroRequisicion,
      obraId: req.obraId,
      estado: 'Abierta',
      fechaCreacion: new Date().toISOString(),
      fechaLimiteCotizacion: limite.toISOString(),
      items: req.items.map((i) => ({
        articuloId: i.articuloId,
        descripcion: i.descripcion,
        cantidad: i.cantidad,
        unidadMedida: i.unidadMedida,
      })),
    }
    licitacionesMock.push(nueva)
    return HttpResponse.json(nueva, { status: 201 })
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
      items?: ItemCotizacionMock[]
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
      items: completarItemsCotizacionMock(body.items, lic.items),
      puntajeTotal: 0,
      esGanadora: false,
    }

    cotizacionesMock.push(nuevaCotizacion)
    return HttpResponse.json(
      { ...nuevaCotizacion, coberturaParcial: nuevaCotizacion.items.some((i) => i.coberturaParcial) },
      { status: 201 },
    )
  }),

  // Contrato: GET /api/cotizaciones?licitacionId={guid}&pesoPrecio=&pesoFlete=&pesoPlazo=&pesoCalificacion=
  // → 200 CotizacionResponseDto[] | 400 ProblemDetails
  // Los pesos son opcionales (por defecto 0,50 / 0,20 / 0,20 / 0,10); cada uno >= 0 y la suma > 0.
  // El mock recalcula el puntaje de forma simplificada (no es la fórmula exacta del servicio).
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

    const leerPeso = (nombre: string, porDefecto: number) => {
      const crudo = url.searchParams.get(nombre)
      return crudo === null ? porDefecto : Number(crudo)
    }
    const pesos = {
      precio: leerPeso('pesoPrecio', 0.5),
      flete: leerPeso('pesoFlete', 0.2),
      plazo: leerPeso('pesoPlazo', 0.2),
      calificacion: leerPeso('pesoCalificacion', 0.1),
    }
    const suma = pesos.precio + pesos.flete + pesos.plazo + pesos.calificacion
    if (Object.values(pesos).some((p) => !Number.isFinite(p) || p < 0) || suma <= 0) {
      return HttpResponse.json(
        {
          title: 'Datos inválidos',
          status: 400,
          detail: 'Los pesos deben ser números de 0 o más y su suma debe ser mayor que 0.',
        },
        { status: 400 },
      )
    }
    const ponderaciones = {
      precio: pesos.precio / suma,
      flete: pesos.flete / suma,
      plazo: pesos.plazo / suma,
      calificacion: pesos.calificacion / suma,
    }

    const filtradas = cotizacionesMock.filter((c) => c.licitacionId === licitacionId)
    // Sin pesos propios ni calificaciones cargadas se devuelven los puntajes fijos del mock,
    // más los pesos por defecto (para que la pantalla muestre el panel de ponderaciones).
    const hayPesosPropios = ['pesoPrecio', 'pesoFlete', 'pesoPlazo', 'pesoCalificacion'].some(
      (nombre) => url.searchParams.has(nombre),
    )
    if (!hayPesosPropios && calificacionesMock.size === 0) {
      return responderPaginado(
        request,
        filtradas.map((c) => ({ ...c, ponderacionesScoring: ponderaciones })),
      )
    }
    const total = (c: CotizacionMock) =>
      c.items.reduce((acc, i) => acc + i.precioUnitario, 0) + (c.flete ?? 0)
    const menor = (valores: number[]) => Math.min(...valores.filter((v) => v > 0), Infinity)
    const minTotal = menor(filtradas.map(total))
    const minFlete = menor(filtradas.map((c) => c.flete ?? 0))
    const minPlazo = menor(filtradas.map((c) => c.plazoEntregaDias ?? 0))
    const conPuntaje = filtradas.map((c) => {
      const calificacion = promedioDeProveedor(c.proveedorId)
      // Qué tan buena es la oferta en cada criterio (0 a 1), sin el peso.
      const crudos = {
        precio: minTotal / total(c),
        flete: (c.flete ?? 0) > 0 ? minFlete / (c.flete ?? 1) : 1,
        plazo: (c.plazoEntregaDias ?? 0) > 0 ? minPlazo / (c.plazoEntregaDias ?? 1) : 1,
        // Igual que el servicio: (calificación - 1) / 4, o 0 si no tiene; su peso igual cuenta en la suma.
        calificacion: calificacion === null ? 0 : (calificacion - 1) / 4,
      }
      const puntos = {
        precio: 100 * ponderaciones.precio * crudos.precio,
        flete: 100 * ponderaciones.flete * crudos.flete,
        plazo: 100 * ponderaciones.plazo * crudos.plazo,
        calificacion: 100 * ponderaciones.calificacion * crudos.calificacion,
      }
      const puntajeTotal =
        Math.round((puntos.precio + puntos.flete + puntos.plazo + puntos.calificacion) * 100) / 100
      const desgloseScoring = {
        puntajePrecio: 100 * crudos.precio,
        puntajeFlete: 100 * crudos.flete,
        puntajePlazo: 100 * crudos.plazo,
        puntajeCalificacion: 100 * crudos.calificacion,
        pesoPrecio: ponderaciones.precio,
        pesoFlete: ponderaciones.flete,
        pesoPlazo: ponderaciones.plazo,
        pesoCalificacion: ponderaciones.calificacion,
      }
      return {
        ...c,
        calificacionProveedor: calificacion,
        desglosePuntaje: puntos,
        desgloseScoring,
        ponderacionesScoring: ponderaciones,
        puntajeTotal,
      }
    })
    const mejor = Math.max(...conPuntaje.map((c) => c.puntajeTotal), 0)
    return responderPaginado(
      request,
      conPuntaje.map((c) => ({ ...c, esGanadora: c.puntajeTotal === mejor })),
    )
  }),

  // Contrato: PUT /api/cotizaciones/proveedores/{proveedorId}/calificacion con body {"calificacion": 1..5}
  // → 200 CalificacionProveedorResponseDto | 400 | 401 | 403 ProblemDetails
  http.put('*/api/cotizaciones/proveedores/:proveedorId/calificacion', async ({ params, request }) => {
    const cuerpo = (await request.json()) as { calificacion?: unknown }
    const valor = cuerpo.calificacion
    if (typeof valor !== 'number' || !Number.isInteger(valor) || valor < 1 || valor > 5) {
      return HttpResponse.json(
        {
          title: 'Datos inválidos',
          status: 400,
          detail: 'La calificación debe ser un número entero entre 1 y 5.',
        },
        { status: 400 },
      )
    }
    const proveedorId = params.proveedorId as string
    // Cada usuario (el token) tiene una sola calificación por proveedor: volver a calificar la reemplaza,
    // pero la de otro usuario se suma y el proveedor queda con el promedio.
    const usuario = request.headers.get('authorization') ?? 'sin-token'
    const porUsuario = calificacionesMock.get(proveedorId) ?? new Map<string, number>()
    porUsuario.set(usuario, valor)
    calificacionesMock.set(proveedorId, porUsuario)
    return HttpResponse.json({
      proveedorId,
      calificacion: promedioDeProveedor(proveedorId),
      cantidadCalificaciones: porUsuario.size,
      fechaActualizacion: new Date().toISOString(),
    })
  }),

  // Contrato: POST /api/licitaciones/{licitacionId}/adjudicaciones con body {"cotizacionId": "<guid>"}
  // → 200 OrdenCompraResponseDto | 400 | 404 | 409 ProblemDetails
  // Contrato: GET /api/cotizaciones/mias → 200 MiCotizacionDto[] (solo las del proveedor de la sesión)
  // Contrato: GET /api/cotizaciones/proveedores/mi-reputacion → 200 ReputacionProveedorResponseDto (#222).
  // Sin calificaciones: calificacion null, cantidad 0 y comentarios vacíos.
  http.get('*/api/cotizaciones/proveedores/mi-reputacion', () =>
    HttpResponse.json({
      proveedorId: USUARIOS_MOCK.find((u) => u.rol === 'Proveedor')?.id ?? '',
      calificacion: null,
      cantidadCalificaciones: 0,
      fechaActualizacion: null,
      ultimosComentarios: [],
    }),
  ),

  http.get('*/api/cotizaciones/mias', ({ request }) => {
    const proveedorId = USUARIOS_MOCK.find((u) => u.rol === 'Proveedor')?.id
    const mias = cotizacionesMock
      .filter((c) => c.proveedorId === proveedorId)
      .map((c) => {
        const lic = licitacionesMock.find((l) => l.licitacionId === c.licitacionId)
        const subtotal = c.items.reduce((acc, i) => acc + (i.subtotalItem ?? i.precioUnitario), 0)
        return {
          cotizacionId: c.cotizacionId,
          licitacionId: c.licitacionId,
          proveedorId: c.proveedorId,
          numeroRequisicion: lic?.numeroRequisicion ?? '',
          obraId: lic?.obraId ?? '',
          fechaLimiteCotizacion: lic?.fechaLimiteCotizacion ?? '',
          estadoLicitacion: lic?.estado ?? 'Abierta',
          subtotalItems: subtotal,
          flete: c.flete ?? 0,
          costoTotal: subtotal + (c.flete ?? 0),
          coberturaParcial: c.items.some((i) => i.coberturaParcial),
          plazoEntregaDias: c.plazoEntregaDias ?? 0,
          fechaCreacion: c.fechaCreacion,
          items: c.items,
          esGanadora: c.esGanadora,
          estadoResultado: c.esGanadora ? 'Gano' : lic?.estado === 'Abierta' ? 'Enviada' : 'NoGano',
        }
      })
    return responderPaginado(request, mias)
  }),

  // Contrato: PUT /api/cotizaciones/{id} → 200 | 400 | 403 | 404 ProblemDetails
  http.put('*/api/cotizaciones/:id', async ({ params, request }) => {
    const cot = cotizacionesMock.find((c) => c.cotizacionId === params.id)
    if (!cot) return HttpResponse.json({ title: 'No encontrada', status: 404 }, { status: 404 })
    const lic = licitacionesMock.find((l) => l.licitacionId === cot.licitacionId)
    if (!lic || lic.estado !== 'Abierta') {
      return HttpResponse.json(
        { title: 'Datos inválidos', status: 400, detail: 'La licitación ya no recibe cotizaciones.' },
        { status: 400 },
      )
    }
    const body = (await request.json().catch(() => ({}))) as {
      flete?: number
      plazoEntregaDias?: number
      items?: ItemCotizacionMock[]
    }
    cot.flete = body.flete ?? cot.flete
    cot.plazoEntregaDias = body.plazoEntregaDias ?? cot.plazoEntregaDias
    cot.items = body.items ? completarItemsCotizacionMock(body.items, lic.items) : cot.items
    return HttpResponse.json(cot, { status: 200 })
  }),

  // Contrato: GET /api/proveedores/{id}/calificacion → 200 | 404 (sin calificaciones todavía)
  http.get('*/api/proveedores/:proveedorId/calificacion', ({ params }) => {
    const proveedorId = params.proveedorId as string
    const promedio = promedioDeProveedor(proveedorId)
    if (promedio === null) return HttpResponse.json({ title: 'No encontrada', status: 404 }, { status: 404 })
    return HttpResponse.json({
      proveedorId,
      calificacion: promedio,
      cantidadCalificaciones: calificacionesMock.get(proveedorId)?.size ?? 0,
      fechaActualizacion: new Date().toISOString(),
    })
  }),

  // Contrato: GET /api/ordenes-compra/precios-historicos?articuloIds=… → 200 PrecioHistoricoDto[]
  // Simula que el primer insumo del catálogo ya se compró antes, a $1.000 menos que lo habitual de las ofertas de ejemplo.
  http.get('*/api/ordenes-compra/precios-historicos', ({ request }) => {
    const ids = new URL(request.url).searchParams.getAll('articuloIds')
    return HttpResponse.json(
      ids
        .filter((id) => id === MOCK_CATALOGO[0].articuloId)
        .map((articuloId) => ({ articuloId, ultimoPrecioUnitario: 8000, fechaEmision: '2026-10-01T12:00:00Z' })),
    )
  }),

  // Contrato: POST /api/licitaciones/{id}/extensiones → 200 | 400 | 404 | 409 ProblemDetails
  http.post('*/api/licitaciones/:id/extensiones', async ({ params, request }) => {
    const licitacion = licitacionesMock.find((l) => l.licitacionId === params.id)
    if (!licitacion) return HttpResponse.json({ title: 'No encontrada', status: 404 }, { status: 404 })
    if (licitacion.estado !== 'Abierta') {
      return HttpResponse.json({ title: 'Conflicto', status: 409 }, { status: 409 })
    }
    const body = (await request.json().catch(() => ({}))) as { fechaLimiteCotizacion?: string }
    const nueva = body.fechaLimiteCotizacion ? new Date(body.fechaLimiteCotizacion) : null
    if (!nueva || Number.isNaN(nueva.getTime()) || nueva <= new Date(licitacion.fechaLimiteCotizacion)) {
      return HttpResponse.json(
        { title: 'Solicitud inválida', status: 400, detail: 'La nueva fecha límite tiene que ser posterior a la actual.' },
        { status: 400 },
      )
    }
    licitacion.fechaLimiteCotizacion = nueva.toISOString()
    return HttpResponse.json(licitacion, { status: 200 })
  }),

  // Contrato: POST /api/licitaciones/{id}/magic-links → 200 GenerarMagicLinkResponseDto | 400 | 404 | 409 (#252)
  http.post('*/api/licitaciones/:id/magic-links', async ({ params, request }) => {
    const licitacion = licitacionesMock.find((l) => l.licitacionId === params.id)
    if (!licitacion) return HttpResponse.json({ title: 'Licitación no encontrada', status: 404 }, { status: 404 })
    const body = (await request.json().catch(() => ({}))) as { proveedorId?: string }
    if (!body.proveedorId) {
      return HttpResponse.json(
        { title: 'Datos de enlace inválidos', status: 400, detail: 'El identificador del proveedor es obligatorio.' },
        { status: 400 },
      )
    }
    if (licitacion.estado !== 'Abierta') {
      return HttpResponse.json({ title: 'Licitación no disponible para generar enlace', status: 409 }, { status: 409 })
    }
    magicLinksGenerados += 1
    const token = `token-simulado-${magicLinksGenerados}`
    return HttpResponse.json(
      {
        token,
        hash: token,
        url: `/api/licitaciones/magic-links/${token}/cotizaciones`,
        fechaDeVencimiento: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
        expiresIn: '1 hour',
        horasVigencia: 1,
      },
      { status: 200 },
    )
  }),

  // Contrato: POST /api/licitaciones/{id}/cierres → 200 | 400 | 404 | 409 ProblemDetails
  http.post('*/api/licitaciones/:id/cierres', async ({ params, request }) => {
    const licitacion = licitacionesMock.find((l) => l.licitacionId === params.id)
    if (!licitacion) return HttpResponse.json({ title: 'No encontrada', status: 404 }, { status: 404 })
    if (licitacion.estado !== 'Abierta') {
      return HttpResponse.json({ title: 'Conflicto', status: 409 }, { status: 409 })
    }
    const body = (await request.json().catch(() => ({}))) as { motivo?: string }
    if (!body.motivo?.trim()) {
      return HttpResponse.json({ title: 'Solicitud inválida', status: 400, detail: 'El motivo de cierre es obligatorio.' }, { status: 400 })
    }
    licitacion.estado = 'Cerrada'
    return HttpResponse.json(licitacion, { status: 200 })
  }),

  http.post('*/api/licitaciones/:licitacionId/adjudicaciones', async ({ params, request }) => {
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

  // ── Presupuesto y Control Presupuestario (#97) ─────────────────────────────
  // Contrato: GET /api/presupuestos/partidas?obraId=&etapa=&pagina=&tamano= → 200 PartidaPresupuestariaDto[]
  http.get('*/api/presupuestos/partidas', ({ request }) => {
    const url = new URL(request.url)
    const obraId = url.searchParams.get('obraId')
    const etapa = url.searchParams.get('etapa')
    const filtradas = partidasMock.filter((p) => {
      if (obraId && p.obraId !== obraId) return false
      if (etapa && p.etapa !== etapa) return false
      return true
    })
    return responderPaginado(request, filtradas)
  }),

  // Contrato: POST /api/presupuestos/partidas → 201 PartidaPresupuestariaDto (Admin, Comprador).
  // 400 datos inválidos, 403 rol sin permiso, 409 código repetido en la obra (#161).
  http.post('*/api/presupuestos/partidas', async ({ request }) => {
    const rol = rolDelJwt(request)
    if (!rol) {
      return HttpResponse.json(
        { title: 'No autenticado', status: 401, detail: 'Se requiere un token JWT válido.' },
        { status: 401 },
      )
    }
    if (rol !== 'Comprador' && rol !== 'Admin') {
      return HttpResponse.json(
        { title: 'Prohibido', status: 403, detail: 'Solo Comprador o Admin pueden cargar partidas.' },
        { status: 403 },
      )
    }
    const cuerpo = (await request.json().catch(() => null)) as {
      obraId?: string
      etapa?: string | null
      codigoPartida?: string
      descripcion?: string
      montoAsignado?: number
      moneda?: string
    } | null
    if (
      !cuerpo ||
      !cuerpo.obraId ||
      !cuerpo.codigoPartida ||
      !cuerpo.descripcion ||
      typeof cuerpo.montoAsignado !== 'number' ||
      cuerpo.montoAsignado < 0
    ) {
      return HttpResponse.json(
        { title: 'Solicitud inválida', status: 400, detail: 'Datos de la partida inválidos o incompletos.' },
        { status: 400 },
      )
    }
    const repetida = partidasMock.some(
      (p) =>
        p.obraId === cuerpo.obraId &&
        (p.codigoPartida.toLowerCase() === cuerpo.codigoPartida!.toLowerCase() ||
          (cuerpo.etapa != null && p.etapa === cuerpo.etapa)),
    )
    if (repetida) {
      return HttpResponse.json(
        {
          title: 'Conflicto',
          status: 409,
          detail: 'Ya existe una partida con ese código o para esa etapa en la obra.',
        },
        { status: 409 },
      )
    }
    const nueva = {
      id: crypto.randomUUID(),
      obraId: cuerpo.obraId,
      etapa: cuerpo.etapa ?? null,
      codigoPartida: cuerpo.codigoPartida,
      descripcion: cuerpo.descripcion,
      montoAsignado: cuerpo.montoAsignado,
      montoComprometido: 0,
      montoEjecutado: 0,
      montoDisponible: cuerpo.montoAsignado,
      moneda: cuerpo.moneda ?? 'ARS',
      createdAt: new Date().toISOString(),
    }
    partidasMock.push(nueva as (typeof partidasMock)[number])
    return HttpResponse.json(nueva, { status: 201 })
  }),

  // Contrato: GET /api/recepciones/ordenes-bloqueadas?obraId=&pagina=&tamano=
  // → 200 OrdenBloqueadaDto[] con X-Total-Count y encabezados de paginación.
  http.get('*/api/recepciones/ordenes-bloqueadas', ({ request }) => {
    const rol = rolDelJwt(request)
    if (!rol) {
      return HttpResponse.json(
        { title: 'No autenticado', status: 401, detail: 'Se requiere un token JWT válido.' },
        { status: 401 },
      )
    }
    if (rol !== 'Comprador' && rol !== 'Admin') {
      return HttpResponse.json(
        { title: 'Acceso denegado', status: 403, detail: 'El usuario no tiene un rol habilitado.' },
        { status: 403 },
      )
    }
    const url = new URL(request.url)
    const obraId = url.searchParams.get('obraId')
    const filtradas = ordenesBloqueadasMock.filter((orden) => !obraId || orden.obraId === obraId)
    return responderPaginado(request, filtradas)
  }),

  // Contrato: POST /api/recepciones/ordenes/{id}/liberaciones → 200 | 400 | 401 | 403 | 404 | 409
  http.post('*/api/recepciones/ordenes/:id/liberaciones', async ({ params, request }) => {
    if (!request.headers.get('Authorization')) {
      return HttpResponse.json(
        {
          type: 'https://tools.ietf.org/html/rfc7235#section-3.1',
          title: 'No autenticado',
          status: 401,
          detail: 'Se requiere un token JWT valido para acceder a este recurso.',
        },
        { status: 401 },
      )
    }

    if (!esUsuarioAdmin(request)) {
      return HttpResponse.json(
        {
          type: 'https://tools.ietf.org/html/rfc7231#section-6.5.3',
          title: 'Acceso denegado',
          status: 403,
          detail: 'El usuario no tiene el rol Admin requerido para autorizar y liberar órdenes de compra.',
        },
        { status: 403 },
      )
    }

    const id = String(params.id)
    if (id === ID_ORDEN_PRUEBA_404 || id === 'oc-inexistente-404') {
      return HttpResponse.json(
        {
          type: 'https://tools.ietf.org/html/rfc7231#section-6.5.4',
          title: 'No encontrado',
          status: 404,
          detail: 'La orden de compra no existe.',
        },
        { status: 404 },
      )
    }

    if (
      id === ID_ORDEN_PRUEBA_409 ||
      id === 'oc-no-bloqueada-409' ||
      ordenesLiberadasMockIds.has(id)
    ) {
      return HttpResponse.json(
        {
          type: 'https://tools.ietf.org/html/rfc7231#section-6.5.8',
          title: 'Conflicto',
          status: 409,
          detail: 'La orden de compra no se encuentra en estado Bloqueada.',
        },
        { status: 409 },
      )
    }

    let body: { motivo?: string | null } = {}
    try {
      body = (await request.json()) as { motivo?: string | null }
    } catch {
      // cuerpo opcional
    }

    if (body.motivo && body.motivo.length > 500) {
      return HttpResponse.json(
        {
          type: 'https://tools.ietf.org/html/rfc7231#section-6.5.1',
          title: 'Datos de solicitud inválidos',
          status: 400,
          detail: 'El motivo no puede superar los 500 caracteres.',
        },
        { status: 400 },
      )
    }

    const orden = ordenesBloqueadasMock.find((o) => o.ordenCompraId === id)
    if (!orden) {
      return HttpResponse.json(
        {
          type: 'https://tools.ietf.org/html/rfc7231#section-6.5.4',
          title: 'No encontrado',
          status: 404,
          detail: 'La orden de compra no existe.',
        },
        { status: 404 },
      )
    }

    ordenesLiberadasMockIds.add(id)
    ordenesBloqueadasMock = ordenesBloqueadasMock.filter((o) => o.ordenCompraId !== id)

    const partida = partidasMock.find((p) => p.etapa === orden.etapa && p.obraId === orden.obraId)
    const montoComprometidoPartida = partida
      ? partida.montoComprometido + orden.montoOrdenCompra
      : orden.montoComprometidoActual + orden.montoOrdenCompra

    if (partida) {
      partida.montoComprometido = montoComprometidoPartida
      partida.montoDisponible = partida.montoAsignado - partida.montoComprometido
    }

    return HttpResponse.json(
      {
        ordenCompraId: id,
        obraId: orden.obraId,
        estado: 1, // 1 = Activa
        liberadaPorUsuarioId: 'a1b2c3d4-0000-0000-0000-000000000004',
        liberadaEn: new Date().toISOString(),
        motivoLiberacion: body.motivo ?? null,
        montoTotal: orden.montoOrdenCompra,
        partidaPresupuestariaId: partida?.id ?? null,
        montoComprometidoPartida,
      },
      { status: 200 },
    )
  }),
]

// ── Estado simulado de Presupuesto y Órdenes Bloqueadas (#97) ───────────────
export const ID_ORDEN_PRUEBA_404 = '00000000-0000-0000-0000-000000000404'
export const ID_ORDEN_PRUEBA_409 = '00000000-0000-0000-0000-000000000409'
export const ORDEN_BLOQUEADA_1_ID = 'e1e2e3e4-0000-0000-0000-0000000000b1'
export const ORDEN_BLOQUEADA_2_ID = 'e1e2e3e4-0000-0000-0000-0000000000b2'

export const MOCK_TOKEN_ADMIN =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9' +
  '.eyJzdWIiOiJhMWIyYzNkNC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDQiLCJlbWFpbCI6ImFkbWluQG9icmFzeW5jLmNvbSIsIm5hbWUiOiJBZG1pbiBPYnJhU3luYyIsInJvbCI6IkFkbWluIn0' +
  '.MOCK_FIRMA_NO_VALIDA'

function crearPartidasMock() {
  return [
    {
      id: 'p1p2p3p4-0000-0000-0000-000000000001',
      obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1', // Torre Alvear
      etapa: 'Estructura',
      codigoPartida: 'EST-001',
      descripcion: 'Hormigón armado y estructuras',
      montoAsignado: 10000000,
      montoComprometido: 6000000,
      montoEjecutado: 4000000,
      montoDisponible: 4000000, // Disponible positivo
      moneda: 'ARS',
      createdAt: '2026-10-01T08:00:00Z',
    },
    {
      id: 'p1p2p3p4-0000-0000-0000-000000000002',
      obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1', // Torre Alvear
      etapa: 'Albañilería',
      codigoPartida: 'ALB-001',
      descripcion: 'Mampostería y tabiques',
      montoAsignado: 5000000,
      montoComprometido: 6200000,
      montoEjecutado: 3000000,
      montoDisponible: -1200000, // Disponible negativo (desvío)
      moneda: 'ARS',
      createdAt: '2026-10-01T08:00:00Z',
    },
  ]
}

function crearOrdenesBloqueadasMock() {
  return [
    {
      ordenCompraId: ORDEN_BLOQUEADA_1_ID,
      obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1',
      partidaId: 'p1p2p3p4-0000-0000-0000-000000000002',
      etapa: 'Albañilería',
      montoOrdenCompra: 2200000,
      montoAsignadoPartida: 5000000,
      montoComprometidoActual: 4000000,
      montoProyectado: 6200000,
      exceso: 1200000,
      motivo: 'La orden de compra por $ 2.200.000,00 supera la partida de Albañilería (exceso de $ 1.200.000,00)',
      fechaBloqueo: '2026-10-20T12:00:00Z',
    },
    {
      ordenCompraId: ORDEN_BLOQUEADA_2_ID,
      obraId: 'b1b2c3d4-0000-0000-0000-0000000000a1',
      partidaId: 'p1p2p3p4-0000-0000-0000-000000000001',
      etapa: 'Estructura',
      montoOrdenCompra: 5500000,
      montoAsignadoPartida: 10000000,
      montoComprometidoActual: 6000000,
      montoProyectado: 11500000,
      exceso: 1500000,
      motivo: 'La orden de compra por $ 5.500.000,00 supera la partida de Estructura (exceso de $ 1.500.000,00)',
      fechaBloqueo: '2026-10-21T12:00:00Z',
    },
  ]
}

let partidasMock = crearPartidasMock()
let ordenesBloqueadasMock = crearOrdenesBloqueadasMock()
let ordenesLiberadasMockIds = new Set<string>()

export function reiniciarPresupuestoMock() {
  partidasMock = crearPartidasMock()
  ordenesBloqueadasMock = crearOrdenesBloqueadasMock()
  ordenesLiberadasMockIds = new Set<string>()
}

function esUsuarioAdmin(request: Request): boolean {
  const auth = request.headers.get('Authorization')
  if (!auth) return false
  const token = auth.replace(/^Bearer\s+/i, '').trim()
  if (/admin/i.test(token)) return true
  try {
    const parts = token.split('.')
    if (parts.length >= 2) {
      const json = atob(parts[1])
      const payload = JSON.parse(json)
      if (payload.rol === 'Admin' || payload.role === 'Admin') return true
    }
  } catch {
    // ignorar error de parseo
  }
  try {
    if (typeof localStorage !== 'undefined') {
      const raw = localStorage.getItem('obrasync_session')
      if (raw) {
        const sesion = JSON.parse(raw)
        if (sesion?.usuario?.rol === 'Admin') return true
      }
    }
  } catch {
    // ignorar
  }
  return false
}

/** Lee el rol del JWT del mecanismo de sesión simulado existente. */
function rolDelJwt(request: Request): string | undefined {
  const auth = request.headers.get('Authorization')
  const coincidencia = auth?.match(/^Bearer\s+([^.]+\.([^.]+)\.[^.]+)$/i)
  if (!coincidencia) return undefined
  try {
    const payload = JSON.parse(atob(coincidencia[2]))
    const rol = payload.rol ?? payload.role
    return typeof rol === 'string' && rol.length > 0 ? rol : undefined
  } catch {
    return undefined
  }
}

