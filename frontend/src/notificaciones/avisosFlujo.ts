import type {
  LicitacionResponseDto,
  MiCotizacionDto,
  OrdenCompraListadoDto,
  ReputacionProveedorDto,
} from '../licitaciones/licitacionesTipos'
import type { OrdenPendiente } from '../recepciones/recepcionesTipos'
import { fechaLarga } from '../requisiciones/formatoFecha'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import { HORAS_LICITACION_POR_VENCER } from './configuracionAvisos'
import type { Vistos } from './novedades'

// Avisos del flujo Requisición → Licitación → Orden de Compra → Recepción (#219).
// Todo lo de este archivo son funciones puras: reciben los datos que ya devolvieron las listas del
// backend y devuelven los avisos. Así las reglas de cada rol se prueban sin red ni pantallas.
//
// Los avisos de requisiciones aprobadas o rechazadas del Capataz y de licitaciones nuevas del Proveedor
// siguen en novedades.ts (#163). Acá está todo lo demás.

/** Quién recibe qué: Compras es el Comprador y también el Admin (los dos operan el mismo flujo). */
export type RolDeAvisos = 'capataz' | 'compras' | 'proveedor'

export function rolDeAvisosDeFlujo(rol: string | undefined): RolDeAvisos | null {
  if (rol === 'Capataz') return 'capataz'
  if (rol === 'Comprador' || rol === 'Admin') return 'compras'
  if (rol === 'Proveedor') return 'proveedor'
  return null
}

/** Clave de localStorage de lo ya visto: una por rol y por usuario (como en novedades.ts). */
export function claveVistosFlujo(rol: RolDeAvisos, usuarioId: string): string {
  return `obrasync_avisos_flujo_${rol}_${usuarioId}`
}

/**
 * "Para hacer": hay algo pendiente de hacer. Se calcula siempre desde los datos del servidor, así que
 * desaparece solo cuando la persona lo hace (no hace falta marcarlo como visto).
 * "Novedades": algo que pasó. Se recuerda como visto en este navegador.
 */
export type SeccionAviso = 'para-hacer' | 'novedades'

export type TipoAviso =
  // Para hacer
  | 'requisicion-por-licitar'
  | 'licitacion-por-vencer'
  | 'orden-por-despachar'
  | 'orden-problema'
  | 'orden-chat'
  | 'orden-calificar'
  | 'recepcion-parcial'
  // Novedades
  | 'cotizacion-nueva'
  | 'orden-despachada'
  | 'oferta-adjudicada'
  | 'recepcion-completa'
  | 'calificacion-recibida'

/** Varios avisos de la misma orden se muestran juntos bajo un título. */
export interface GrupoAviso {
  /** Id de la orden de compra. */
  id: string
  /** "Orden 1A2B3C4D". */
  titulo: string
}

export interface Aviso {
  /** Único: "<tipo>:<id de lo que pasó>". */
  id: string
  /** Lo que se recuerda al marcarlo como visto. Si cambia (otra cotización, otro despacho), vuelve a avisar. */
  marca: string
  tipo: TipoAviso
  seccion: SeccionAviso
  /** Frase completa, con el nombre de la orden o la requisición. */
  texto: string
  /** La misma frase sin nombrar la orden, para cuando va bajo el título del grupo. */
  textoCorto: string
  /** Dato de apoyo (el motivo del problema, la fecha estimada de llegada). */
  detalle?: string
  /** A dónde lleva el aviso. */
  ruta: string
  grupo?: GrupoAviso
}

/** Lo que devolvieron las listas del backend. Una lista sin definir es una que no se pudo consultar. */
export interface DatosAvisos {
  /** Compras: requisiciones con estado Aprobada. */
  requisicionesAprobadas?: RequisicionResponseDto[]
  /** Compras: todas las licitaciones. */
  licitaciones?: LicitacionResponseDto[]
  /** Compras: cuántas cotizaciones tiene cada licitación abierta (id de licitación → cantidad). */
  cotizacionesPorLicitacion?: Record<string, number>
  /** Compras y Proveedor: GET /api/ordenes-compra (cada uno ve las que le corresponden). */
  ordenesCompra?: OrdenCompraListadoDto[]
  /** Proveedor: GET /api/cotizaciones/mias. */
  misCotizaciones?: MiCotizacionDto[]
  /** Proveedor: GET /api/cotizaciones/proveedores/mi-reputacion. */
  reputacion?: ReputacionProveedorDto
  /** Capataz: órdenes todavía no recibidas por completo (GET /api/recepciones/ordenes-pendientes). */
  ordenesPendientes?: OrdenPendiente[]
}

/** Nombre corto de una orden: los primeros 8 caracteres de su id (igual que en las pantallas de órdenes). */
export function codigoDeOrden(id: string): string {
  return id.slice(0, 8).toUpperCase()
}

function grupoDeOrden(ordenId: string): GrupoAviso {
  return { id: ordenId, titulo: `Orden ${codigoDeOrden(ordenId)}` }
}

function textoMensajes(cantidad: number): string {
  return cantidad === 1 ? '1 mensaje sin leer' : `${cantidad} mensajes sin leer`
}

// ---------------------------------------------------------------------------------------------
// Compras (Comprador y Admin)
// ---------------------------------------------------------------------------------------------

const ESTADOS_YA_LICITADA = ['Abierta', 'Adjudicada']

function avisosDeComprasPorRequisiciones(datos: DatosAvisos, ahora: number, vistos: Vistos): Aviso[] {
  const avisos: Aviso[] = []
  const { requisicionesAprobadas, licitaciones, cotizacionesPorLicitacion } = datos

  // Aprobada y todavía sin licitación: Compras tiene que publicarla, y hasta entonces no se puede cotizar.
  if (requisicionesAprobadas && licitaciones) {
    const yaLicitadas = new Set(
      licitaciones.filter((l) => ESTADOS_YA_LICITADA.includes(l.estado)).map((l) => l.requisicionId),
    )
    for (const req of requisicionesAprobadas) {
      if (yaLicitadas.has(req.requisicionId)) continue
      const texto = `La requisición ${req.numeroRequisicion} está aprobada y espera su licitación`
      avisos.push({
        id: `requisicion-por-licitar:${req.requisicionId}`,
        marca: '',
        tipo: 'requisicion-por-licitar',
        seccion: 'para-hacer',
        texto,
        textoCorto: texto,
        ruta: `/compras/requisiciones/historial/${encodeURIComponent(req.requisicionId)}`,
      })
    }
  }

  if (licitaciones) {
    const limiteMs = HORAS_LICITACION_POR_VENCER * 60 * 60 * 1000
    for (const lic of licitaciones) {
      if (lic.estado !== 'Abierta') continue
      const nombre = lic.numeroRequisicion ?? 'Licitación'
      const ruta = `/compras/licitaciones?licitacionId=${encodeURIComponent(lic.licitacionId)}`

      // Por vencer: queda poco tiempo (y todavía no venció).
      const faltaMs = Date.parse(lic.fechaLimiteCotizacion) - ahora
      if (faltaMs > 0 && faltaMs <= limiteMs) {
        const texto = `La licitación ${nombre} vence en menos de ${HORAS_LICITACION_POR_VENCER} horas`
        avisos.push({
          id: `licitacion-por-vencer:${lic.licitacionId}`,
          marca: '',
          tipo: 'licitacion-por-vencer',
          seccion: 'para-hacer',
          texto,
          textoCorto: texto,
          ruta,
        })
      }

      // Cotización nueva: la marca es la cantidad de cotizaciones; si sube, vuelve a avisar.
      const cantidad = cotizacionesPorLicitacion?.[lic.licitacionId] ?? 0
      if (cantidad > 0) {
        const id = `cotizacion-nueva:${lic.licitacionId}`
        const yaVistas = Number(vistos[id])
        const nuevas = Number.isFinite(yaVistas) ? Math.max(cantidad - yaVistas, 1) : cantidad
        const texto =
          nuevas === 1
            ? `Llegó 1 cotización nueva a la licitación ${nombre}`
            : `Llegaron ${nuevas} cotizaciones nuevas a la licitación ${nombre}`
        avisos.push({
          id,
          marca: String(cantidad),
          tipo: 'cotizacion-nueva',
          seccion: 'novedades',
          texto,
          textoCorto: texto,
          ruta,
        })
      }
    }
  }

  return avisos
}

function avisosDeOrdenes(
  rol: 'compras' | 'proveedor',
  ordenes: OrdenCompraListadoDto[],
): Aviso[] {
  const avisos: Aviso[] = []
  const rutaOrdenes = rol === 'compras' ? '/compras/ordenes' : '/proveedores/mis-ordenes'

  function agregar(
    orden: OrdenCompraListadoDto,
    tipo: TipoAviso,
    seccion: SeccionAviso,
    marca: string,
    texto: string,
    textoCorto: string,
    detalle?: string,
  ) {
    avisos.push({
      id: `${tipo}:${orden.id}`,
      marca,
      tipo,
      seccion,
      texto,
      textoCorto,
      ...(detalle ? { detalle } : {}),
      ruta: rutaOrdenes,
      grupo: grupoDeOrden(orden.id),
    })
  }

  for (const orden of ordenes) {
    const codigo = codigoDeOrden(orden.id)
    const noLeidos = orden.mensajesNoLeidos ?? 0

    // Lo más urgente primero dentro de cada orden: problema, mensajes, calificar, despachar.
    if (orden.tieneProblemaAbierto) {
      agregar(
        orden,
        'orden-problema',
        'para-hacer',
        '',
        `Entrega con problema en la orden ${codigo}`,
        'Entrega con problema',
        orden.motivoProblemaAbierto ?? undefined,
      )
    }

    if (noLeidos > 0) {
      agregar(
        orden,
        'orden-chat',
        'para-hacer',
        '',
        `${textoMensajes(noLeidos)} en el chat de la orden ${codigo}`,
        `${textoMensajes(noLeidos)} en el chat`,
      )
    }

    if (rol === 'compras') {
      if (orden.recibidaCompleta === true && (orden.miCalificacion === null || orden.miCalificacion === undefined)) {
        agregar(
          orden,
          'orden-calificar',
          'para-hacer',
          '',
          `Llegó todo el pedido de la orden ${codigo}: calificá al proveedor`,
          'Llegó todo el pedido: calificá al proveedor',
        )
      }
      if (orden.fueDespachada) {
        agregar(
          orden,
          'orden-despachada',
          'novedades',
          orden.fechaDespacho ?? 'despachada',
          `El proveedor despachó la orden ${codigo}`,
          'El proveedor despachó el pedido',
          orden.fechaEstimadaLlegada ? `Llega el ${fechaLarga(orden.fechaEstimadaLlegada)}` : undefined,
        )
      }
    } else {
      if (!orden.fueDespachada) {
        // Cubre "te adjudicaron", "orden emitida" y "orden por despachar": es la misma orden y la misma acción.
        agregar(
          orden,
          'orden-por-despachar',
          'para-hacer',
          '',
          `Te adjudicaron la orden ${codigo}: marcala como despachada`,
          'Te la adjudicaron: marcala como despachada',
        )
      }
      if (orden.recibidaCompleta === true) {
        agregar(
          orden,
          'recepcion-completa',
          'novedades',
          orden.fechaRecepcionCompleta ?? 'completa',
          `El Capataz confirmó que llegó todo el pedido de la orden ${codigo}`,
          'El Capataz confirmó que llegó todo el pedido',
        )
      }
    }
  }

  return avisos
}

// ---------------------------------------------------------------------------------------------
// Proveedor
// ---------------------------------------------------------------------------------------------

function avisosDeProveedor(datos: DatosAvisos): Aviso[] {
  const avisos: Aviso[] = []
  const { ordenesCompra, misCotizaciones, reputacion } = datos

  if (ordenesCompra) avisos.push(...avisosDeOrdenes('proveedor', ordenesCompra))

  // Oferta adjudicada: normalmente ya la cubre el aviso de la orden emitida; este solo aparece si la
  // orden todavía no está en la lista, para que el aviso no se duplique.
  if (misCotizaciones && ordenesCompra) {
    const conOrden = new Set(ordenesCompra.map((o) => o.cotizacionId))
    for (const cot of misCotizaciones) {
      if (cot.estadoResultado !== 'Gano' || conOrden.has(cot.cotizacionId)) continue
      const texto = `Tu oferta de la requisición ${cot.numeroRequisicion} fue adjudicada`
      avisos.push({
        id: `oferta-adjudicada:${cot.cotizacionId}`,
        marca: 'gano',
        tipo: 'oferta-adjudicada',
        seccion: 'novedades',
        texto,
        textoCorto: texto,
        ruta: '/proveedores/mis-cotizaciones',
      })
    }
  }

  if (reputacion) {
    for (const comentario of reputacion.ultimosComentarios) {
      const referencia = comentario.ordenCompraId ?? comentario.fechaActualizacion
      const estrellas = comentario.calificacion === 1 ? '1 estrella' : `${comentario.calificacion} estrellas`
      const codigo = comentario.ordenCompraId ? codigoDeOrden(comentario.ordenCompraId) : null
      const comentarioLimpio = comentario.comentario.trim()
      avisos.push({
        id: `calificacion-recibida:${referencia}`,
        marca: comentario.fechaActualizacion,
        tipo: 'calificacion-recibida',
        seccion: 'novedades',
        texto: codigo
          ? `Recibiste ${estrellas} por la orden ${codigo}`
          : `Recibiste una calificación de ${estrellas}`,
        textoCorto: `Recibiste ${estrellas}`,
        ...(comentarioLimpio ? { detalle: comentarioLimpio } : {}),
        ruta: '/proveedores/mis-ordenes',
        ...(comentario.ordenCompraId ? { grupo: grupoDeOrden(comentario.ordenCompraId) } : {}),
      })
    }
  }

  return avisos
}

// ---------------------------------------------------------------------------------------------
// Capataz
// ---------------------------------------------------------------------------------------------

function avisosDeCapataz(datos: DatosAvisos): Aviso[] {
  const avisos: Aviso[] = []
  for (const orden of datos.ordenesPendientes ?? []) {
    const codigo = codigoDeOrden(orden.ordenCompraId)
    const grupo = grupoDeOrden(orden.ordenCompraId)

    // Despachada: el Proveedor ya envió el pedido, con la fecha estimada si la informó (#216).
    if (orden.fechaDespacho) {
      avisos.push({
        id: `orden-despachada:${orden.ordenCompraId}`,
        marca: orden.fechaDespacho,
        tipo: 'orden-despachada',
        seccion: 'novedades',
        texto: `Despacharon tu pedido, orden ${codigo}`,
        textoCorto: 'Despacharon tu pedido',
        ...(orden.fechaEstimadaLlegada ? { detalle: `Llega el ${fechaLarga(orden.fechaEstimadaLlegada)}` } : {}),
        ruta: '/obra/recepcion',
        grupo,
      })
    }

    // Llegó una parte y falta completar la recepción.
    const llegoAlgo = orden.items.some((i) => i.cantidadRecibida > 0)
    const faltaAlgo = orden.items.some((i) => i.cantidadRecibida < i.cantidadEsperada)
    if (llegoAlgo && faltaAlgo) {
      avisos.push({
        id: `recepcion-parcial:${orden.ordenCompraId}`,
        marca: '',
        tipo: 'recepcion-parcial',
        seccion: 'para-hacer',
        texto: `La orden ${codigo} llegó parcial: falta completar la recepción`,
        textoCorto: 'Llegó parcial: falta completar la recepción',
        ruta: '/obra/recepcion',
        grupo,
      })
    }
  }
  return avisos
}

// ---------------------------------------------------------------------------------------------
// Entrada
// ---------------------------------------------------------------------------------------------

/** Todos los avisos posibles de un rol (sin descartar los ya vistos). Un aviso por id. */
export function candidatosDeAvisos(rol: RolDeAvisos, datos: DatosAvisos, ahora: number, vistos: Vistos = {}): Aviso[] {
  let avisos: Aviso[]
  if (rol === 'compras') {
    avisos = [
      ...avisosDeComprasPorRequisiciones(datos, ahora, vistos),
      ...(datos.ordenesCompra ? avisosDeOrdenes('compras', datos.ordenesCompra) : []),
    ]
  } else if (rol === 'proveedor') {
    avisos = avisosDeProveedor(datos)
  } else {
    avisos = avisosDeCapataz(datos)
  }
  const unicos = new Map<string, Aviso>()
  for (const aviso of avisos) unicos.set(aviso.id, aviso)
  return [...unicos.values()]
}

/**
 * Los avisos que hay que mostrar ahora: todo lo "Para hacer" y las "Novedades" que todavía no se vieron.
 * Con vistos en null (primera vez en este navegador y sin saber todavía qué era viejo) solo se muestra
 * lo "Para hacer", para no llenar la campana de avisos viejos.
 */
export function avisosDeFlujo(
  rol: RolDeAvisos,
  datos: DatosAvisos,
  vistos: Vistos | null,
  ahora: number,
): Aviso[] {
  return candidatosDeAvisos(rol, datos, ahora, vistos ?? {}).filter(
    (aviso) => aviso.seccion === 'para-hacer' || (vistos !== null && vistos[aviso.id] !== aviso.marca),
  )
}

/** Primera vez en este navegador: todo lo que ya había pasado se da por visto y solo avisa lo que cambie desde ahora. */
export function vistosInicialesDeFlujo(rol: RolDeAvisos, datos: DatosAvisos, ahora: number): Vistos {
  const vistos: Vistos = {}
  for (const aviso of candidatosDeAvisos(rol, datos, ahora)) {
    if (aviso.seccion === 'novedades') vistos[aviso.id] = aviso.marca
  }
  return vistos
}

/** Agrupa los avisos por orden, respetando el orden en que aparece cada una. Los avisos sin orden van solos. */
export interface RenglonDeAvisos {
  /** Título de la orden si hay 2 o más avisos de la misma; si no, el aviso va solo y completo. */
  titulo?: string
  avisos: Aviso[]
}

export function agruparPorOrden(avisos: Aviso[]): RenglonDeAvisos[] {
  const renglones: RenglonDeAvisos[] = []
  const porOrden = new Map<string, RenglonDeAvisos>()
  for (const aviso of avisos) {
    if (!aviso.grupo) {
      renglones.push({ avisos: [aviso] })
      continue
    }
    const existente = porOrden.get(aviso.grupo.id)
    if (existente) {
      existente.avisos.push(aviso)
      existente.titulo = aviso.grupo.titulo
    } else {
      const nuevo: RenglonDeAvisos = { avisos: [aviso] }
      porOrden.set(aviso.grupo.id, nuevo)
      renglones.push(nuevo)
    }
  }
  return renglones
}
