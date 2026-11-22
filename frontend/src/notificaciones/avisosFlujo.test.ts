import { describe, expect, it } from 'vitest'
import type { LicitacionResponseDto, OrdenCompraListadoDto } from '../licitaciones/licitacionesTipos'
import type { OrdenPendiente } from '../recepciones/recepcionesTipos'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import {
  agruparPorOrden,
  avisosDeFlujo,
  claveVistosFlujo,
  codigoDeOrden,
  rolDeAvisosDeFlujo,
  vistosInicialesDeFlujo,
  type DatosAvisos,
} from './avisosFlujo'

const AHORA = Date.parse('2026-11-22T12:00:00Z')
const ORDEN_A = 'aaaaaaaa-0000-0000-0000-000000000001'
const ORDEN_B = 'bbbbbbbb-0000-0000-0000-000000000002'

function orden(id: string, cambios: Partial<OrdenCompraListadoDto> = {}): OrdenCompraListadoDto {
  return {
    id,
    licitacionId: 'lic-1',
    cotizacionId: `cot-${id}`,
    obraId: 'obra-1',
    proveedorId: 'prov-1',
    estado: 'Emitida',
    fechaEmision: '2026-11-10T10:00:00Z',
    fueDespachada: false,
    ...cambios,
  }
}

function aprobada(id: string): RequisicionResponseDto {
  return {
    requisicionId: id,
    numeroRequisicion: `REQ-${id}`,
    obraId: 'obra-1',
    solicitanteId: 'cap-1',
    estado: 'Aprobada',
    fechaCreacion: '2026-11-01T10:00:00Z',
    items: [],
  }
}

function licitacion(id: string, requisicionId: string, cambios: Partial<LicitacionResponseDto> = {}): LicitacionResponseDto {
  return {
    licitacionId: id,
    requisicionId,
    numeroRequisicion: `REQ-${requisicionId}`,
    obraId: 'obra-1',
    estado: 'Abierta',
    fechaCreacion: '2026-11-05T10:00:00Z',
    fechaLimiteCotizacion: '2026-12-30T10:00:00Z',
    items: [],
    ...cambios,
  }
}

function pendiente(id: string, cambios: Partial<OrdenPendiente> = {}): OrdenPendiente {
  return {
    ordenCompraId: id,
    obraId: 'obra-1',
    items: [{ articuloId: 'a', descripcionArticulo: 'Cemento', cantidadEsperada: 10, cantidadRecibida: 0, unidadMedida: 'bolsa' }],
    ...cambios,
  }
}

const tipos = (rol: Parameters<typeof avisosDeFlujo>[0], datos: DatosAvisos, vistos: Record<string, string> | null = {}) =>
  avisosDeFlujo(rol, datos, vistos, AHORA).map((a) => a.tipo)

describe('roles y claves', () => {
  it('Comprador y Admin comparten los avisos de Compras', () => {
    expect(rolDeAvisosDeFlujo('Comprador')).toBe('compras')
    expect(rolDeAvisosDeFlujo('Admin')).toBe('compras')
    expect(rolDeAvisosDeFlujo('Capataz')).toBe('capataz')
    expect(rolDeAvisosDeFlujo('Proveedor')).toBe('proveedor')
    expect(rolDeAvisosDeFlujo('Otro')).toBeNull()
    expect(rolDeAvisosDeFlujo(undefined)).toBeNull()
  })

  it('la clave de lo visto es por rol y por usuario', () => {
    expect(claveVistosFlujo('compras', 'u-1')).not.toBe(claveVistosFlujo('compras', 'u-2'))
    expect(claveVistosFlujo('compras', 'u-1')).not.toBe(claveVistosFlujo('proveedor', 'u-1'))
  })
})

describe('Compras (Comprador y Admin)', () => {
  it('requisición aprobada sin licitación: aparece en "Para hacer"; con licitación abierta o adjudicada, no', () => {
    const datos: DatosAvisos = {
      requisicionesAprobadas: [aprobada('r1'), aprobada('r2'), aprobada('r3'), aprobada('r4')],
      licitaciones: [
        licitacion('l2', 'r2'),
        licitacion('l3', 'r3', { estado: 'Adjudicada' }),
        licitacion('l4', 'r4', { estado: 'Cerrada' }),
      ],
    }
    const avisos = avisosDeFlujo('compras', datos, {}, AHORA).filter((a) => a.tipo === 'requisicion-por-licitar')

    // r4 tuvo una licitación cerrada sin adjudicar: sigue esperando una nueva.
    expect(avisos.map((a) => a.id)).toEqual(['requisicion-por-licitar:r1', 'requisicion-por-licitar:r4'])
    expect(avisos[0]).toMatchObject({
      seccion: 'para-hacer',
      texto: 'La requisición REQ-r1 está aprobada y espera su licitación',
      ruta: '/compras/requisiciones/historial/r1',
    })
  })

  it('licitación por vencer: solo si falta 24 horas o menos y todavía no venció', () => {
    const datos: DatosAvisos = {
      licitaciones: [
        licitacion('pronto', 'r1', { fechaLimiteCotizacion: '2026-11-23T08:00:00Z' }),
        licitacion('lejos', 'r2', { fechaLimiteCotizacion: '2026-11-25T08:00:00Z' }),
        licitacion('vencida', 'r3', { fechaLimiteCotizacion: '2026-11-21T08:00:00Z' }),
        licitacion('cerrada', 'r4', { estado: 'Cerrada', fechaLimiteCotizacion: '2026-11-23T08:00:00Z' }),
      ],
    }
    const avisos = avisosDeFlujo('compras', datos, {}, AHORA)

    expect(avisos.map((a) => a.id)).toEqual(['licitacion-por-vencer:pronto'])
    expect(avisos[0]).toMatchObject({
      seccion: 'para-hacer',
      ruta: '/compras/licitaciones?licitacionId=pronto',
    })
  })

  it('cotización nueva: avisa con la cantidad y vuelve a avisar cuando llega otra', () => {
    const datos: DatosAvisos = {
      licitaciones: [licitacion('l1', 'r1')],
      cotizacionesPorLicitacion: { l1: 2 },
    }
    const primera = avisosDeFlujo('compras', datos, {}, AHORA)
    expect(primera).toHaveLength(1)
    expect(primera[0]).toMatchObject({
      tipo: 'cotizacion-nueva',
      seccion: 'novedades',
      marca: '2',
      texto: 'Llegaron 2 cotizaciones nuevas a la licitación REQ-r1',
    })

    // Ya vistas: no avisa.
    expect(avisosDeFlujo('compras', datos, { 'cotizacion-nueva:l1': '2' }, AHORA)).toEqual([])

    // Llega una más: avisa solo de la nueva.
    const conOtra: DatosAvisos = { ...datos, cotizacionesPorLicitacion: { l1: 3 } }
    const nueva = avisosDeFlujo('compras', conOtra, { 'cotizacion-nueva:l1': '2' }, AHORA)
    expect(nueva[0].texto).toBe('Llegó 1 cotización nueva a la licitación REQ-r1')
  })

  it('órdenes: despacho (novedad), entrega con problema, chat y calificar (para hacer), agrupados por orden', () => {
    const datos: DatosAvisos = {
      ordenesCompra: [
        orden(ORDEN_A, {
          fueDespachada: true,
          fechaDespacho: '2026-11-20T10:00:00Z',
          fechaEstimadaLlegada: '2026-11-25T10:00:00Z',
          tieneProblemaAbierto: true,
          motivoProblemaAbierto: 'Llegó dañada',
          mensajesNoLeidos: 3,
        }),
        orden(ORDEN_B, { fueDespachada: true, fechaDespacho: '2026-11-19T10:00:00Z', recibidaCompleta: true, miCalificacion: null }),
      ],
    }
    const avisos = avisosDeFlujo('compras', datos, {}, AHORA)

    expect(avisos.map((a) => a.tipo).sort()).toEqual(
      ['orden-calificar', 'orden-chat', 'orden-despachada', 'orden-despachada', 'orden-problema'].sort(),
    )
    const problema = avisos.find((a) => a.tipo === 'orden-problema')
    expect(problema).toMatchObject({ seccion: 'para-hacer', detalle: 'Llegó dañada', ruta: '/compras/ordenes' })
    expect(avisos.find((a) => a.tipo === 'orden-chat')?.texto).toBe(
      `3 mensajes sin leer en el chat de la orden ${codigoDeOrden(ORDEN_A)}`,
    )
    expect(avisos.find((a) => a.tipo === 'orden-calificar')?.texto).toBe(
      `Llegó todo el pedido de la orden ${codigoDeOrden(ORDEN_B)}: calificá al proveedor`,
    )

    // Agrupado: la orden A junta 3 avisos; la B, 2.
    const renglones = agruparPorOrden(avisos)
    expect(renglones).toHaveLength(2)
    expect(renglones.map((r) => r.avisos.length).sort()).toEqual([2, 3])
    expect(renglones.every((r) => r.titulo?.startsWith('Orden '))).toBe(true)
  })

  it('calificar: si ya la calificó, no vuelve a pedirlo', () => {
    const datos: DatosAvisos = { ordenesCompra: [orden(ORDEN_A, { recibidaCompleta: true, miCalificacion: 4 })] }
    expect(tipos('compras', datos)).not.toContain('orden-calificar')
  })

  it('el despacho ya visto no vuelve, pero uno nuevo de otra orden sí', () => {
    const datos: DatosAvisos = {
      ordenesCompra: [
        orden(ORDEN_A, { fueDespachada: true, fechaDespacho: '2026-11-20T10:00:00Z' }),
        orden(ORDEN_B, { fueDespachada: true, fechaDespacho: '2026-11-21T10:00:00Z' }),
      ],
    }
    const vistos = { [`orden-despachada:${ORDEN_A}`]: '2026-11-20T10:00:00Z' }
    expect(avisosDeFlujo('compras', datos, vistos, AHORA).map((a) => a.id)).toEqual([`orden-despachada:${ORDEN_B}`])
  })

  it('Compras no recibe los avisos de Proveedor ni de Capataz', () => {
    const datos: DatosAvisos = { ordenesCompra: [orden(ORDEN_A)], ordenesPendientes: [pendiente(ORDEN_A, { fechaDespacho: '2026-11-20T10:00:00Z' })] }
    expect(tipos('compras', datos)).toEqual([])
  })
})

describe('Proveedor', () => {
  it('orden adjudicada sin despachar: "Para hacer" (cubre adjudicada, emitida y por despachar, sin duplicar)', () => {
    const datos: DatosAvisos = {
      ordenesCompra: [orden(ORDEN_A)],
      misCotizaciones: [
        {
          cotizacionId: `cot-${ORDEN_A}`,
          licitacionId: 'lic-1',
          proveedorId: 'prov-1',
          numeroRequisicion: 'REQ-1',
          obraId: 'obra-1',
          fechaLimiteCotizacion: '2026-11-01T10:00:00Z',
          estadoLicitacion: 'Adjudicada',
          subtotalItems: 1,
          flete: 0,
          costoTotal: 1,
          coberturaParcial: false,
          plazoEntregaDias: 1,
          fechaCreacion: '2026-11-01T10:00:00Z',
          items: [],
          esGanadora: true,
          estadoResultado: 'Gano',
        },
      ],
    }
    const avisos = avisosDeFlujo('proveedor', datos, {}, AHORA)

    expect(avisos.map((a) => a.tipo)).toEqual(['orden-por-despachar'])
    expect(avisos[0]).toMatchObject({ seccion: 'para-hacer', ruta: '/proveedores/mis-ordenes' })
  })

  it('oferta adjudicada sin orden todavía en la lista: avisa como novedad', () => {
    const datos: DatosAvisos = {
      ordenesCompra: [],
      misCotizaciones: [
        {
          cotizacionId: 'cot-9',
          licitacionId: 'lic-9',
          proveedorId: 'prov-1',
          numeroRequisicion: 'REQ-9',
          obraId: 'obra-1',
          fechaLimiteCotizacion: '2026-11-01T10:00:00Z',
          estadoLicitacion: 'Adjudicada',
          subtotalItems: 1,
          flete: 0,
          costoTotal: 1,
          coberturaParcial: false,
          plazoEntregaDias: 1,
          fechaCreacion: '2026-11-01T10:00:00Z',
          items: [],
          esGanadora: true,
          estadoResultado: 'Gano',
        },
      ],
    }
    const avisos = avisosDeFlujo('proveedor', datos, {}, AHORA)
    expect(avisos).toHaveLength(1)
    expect(avisos[0]).toMatchObject({ tipo: 'oferta-adjudicada', seccion: 'novedades', texto: 'Tu oferta de la requisición REQ-9 fue adjudicada' })
  })

  it('el Capataz confirmó la recepción completa, entrega con problema y chat', () => {
    const datos: DatosAvisos = {
      ordenesCompra: [
        orden(ORDEN_A, {
          fueDespachada: true,
          recibidaCompleta: true,
          fechaRecepcionCompleta: '2026-11-21T10:00:00Z',
          tieneProblemaAbierto: true,
          motivoProblemaAbierto: 'No llegó nada',
          mensajesNoLeidos: 1,
        }),
      ],
    }
    const avisos = avisosDeFlujo('proveedor', datos, {}, AHORA)

    expect(avisos.map((a) => `${a.seccion}:${a.tipo}`).sort()).toEqual(
      ['novedades:recepcion-completa', 'para-hacer:orden-chat', 'para-hacer:orden-problema'].sort(),
    )
    expect(avisos.find((a) => a.tipo === 'orden-chat')?.texto).toContain('1 mensaje sin leer')
    // El Proveedor no califica órdenes ni ve avisos de Compras.
    expect(avisos.map((a) => a.tipo)).not.toContain('orden-calificar')
  })

  it('nueva calificación recibida: una novedad por calificación, y vuelve a avisar si la editan', () => {
    const datos: DatosAvisos = {
      reputacion: {
        proveedorId: 'prov-1',
        calificacion: 4.5,
        cantidadCalificaciones: 2,
        ultimosComentarios: [
          { ordenCompraId: ORDEN_A, calificacion: 5, comentario: ' Excelente ', fechaActualizacion: '2026-11-21T10:00:00Z' },
          { ordenCompraId: ORDEN_B, calificacion: 1, comentario: '', fechaActualizacion: '2026-11-20T10:00:00Z' },
        ],
      },
    }
    const avisos = avisosDeFlujo('proveedor', datos, {}, AHORA)
    expect(avisos.map((a) => a.texto)).toEqual([
      `Recibiste 5 estrellas por la orden ${codigoDeOrden(ORDEN_A)}`,
      `Recibiste 1 estrella por la orden ${codigoDeOrden(ORDEN_B)}`,
    ])
    expect(avisos[0].detalle).toBe('Excelente')
    expect(avisos[1].detalle).toBeUndefined()

    const vistos = { [avisos[0].id]: avisos[0].marca, [avisos[1].id]: avisos[1].marca }
    expect(avisosDeFlujo('proveedor', datos, vistos, AHORA)).toEqual([])

    const editada: DatosAvisos = {
      reputacion: {
        ...datos.reputacion!,
        ultimosComentarios: [{ ordenCompraId: ORDEN_A, calificacion: 3, comentario: 'Cambié', fechaActualizacion: '2026-11-22T09:00:00Z' }],
      },
    }
    expect(avisosDeFlujo('proveedor', editada, vistos, AHORA)).toHaveLength(1)
  })
})

describe('Capataz', () => {
  it('pedido despachado con fecha estimada (novedad) y recepción parcial pendiente (para hacer)', () => {
    const datos: DatosAvisos = {
      ordenesPendientes: [
        pendiente(ORDEN_A, { fechaDespacho: '2026-11-20T10:00:00Z', fechaEstimadaLlegada: '2026-11-25T12:00:00Z' }),
        pendiente(ORDEN_B, {
          fechaDespacho: '2026-11-18T10:00:00Z',
          items: [{ articuloId: 'a', descripcionArticulo: 'Cemento', cantidadEsperada: 10, cantidadRecibida: 4, unidadMedida: 'bolsa' }],
        }),
        pendiente('cccccccc-0000-0000-0000-000000000003'),
      ],
    }
    const avisos = avisosDeFlujo('capataz', datos, {}, AHORA)

    expect(avisos.map((a) => `${a.seccion}:${a.tipo}:${a.id.split(':')[1].slice(0, 4)}`)).toEqual([
      'novedades:orden-despachada:aaaa',
      'novedades:orden-despachada:bbbb',
      'para-hacer:recepcion-parcial:bbbb',
    ])
    expect(avisos[0].detalle).toMatch(/^Llega el /)
    expect(avisos[0].ruta).toBe('/obra/recepcion')
    // La orden sin despachar y sin nada recibido no genera avisos.
    expect(avisos.some((a) => a.id.includes('cccccccc'))).toBe(false)
  })

  it('una orden recibida completa (que ya no está pendiente) no genera aviso de parcial', () => {
    const completa = pendiente(ORDEN_A, {
      items: [{ articuloId: 'a', descripcionArticulo: 'Cemento', cantidadEsperada: 10, cantidadRecibida: 10, unidadMedida: 'bolsa' }],
    })
    expect(tipos('capataz', { ordenesPendientes: [completa] })).toEqual([])
  })
})

describe('sin duplicar ni perder avisos', () => {
  it('primera vez: las novedades que ya existían se dan por vistas, y lo "para hacer" sigue apareciendo', () => {
    const datos: DatosAvisos = {
      ordenesCompra: [
        orden(ORDEN_A, { fueDespachada: true, fechaDespacho: '2026-11-20T10:00:00Z', tieneProblemaAbierto: true }),
      ],
    }
    const iniciales = vistosInicialesDeFlujo('compras', datos, AHORA)

    expect(iniciales).toEqual({ [`orden-despachada:${ORDEN_A}`]: '2026-11-20T10:00:00Z' })
    const avisos = avisosDeFlujo('compras', datos, iniciales, AHORA)
    expect(avisos.map((a) => a.tipo)).toEqual(['orden-problema'])
  })

  it('sin saber qué era viejo (vistos null) solo muestra lo "para hacer"', () => {
    const datos: DatosAvisos = {
      ordenesCompra: [orden(ORDEN_A, { fueDespachada: true, fechaDespacho: '2026-11-20T10:00:00Z', mensajesNoLeidos: 2 })],
    }
    expect(tipos('compras', datos, null)).toEqual(['orden-chat'])
  })

  it('cada aviso tiene un id único aunque la lista traiga la misma orden dos veces', () => {
    const datos: DatosAvisos = { ordenesCompra: [orden(ORDEN_A, { mensajesNoLeidos: 1 }), orden(ORDEN_A, { mensajesNoLeidos: 1 })] }
    expect(avisosDeFlujo('compras', datos, {}, AHORA)).toHaveLength(1)
  })

  it('una lista que no se pudo pedir (sin definir) no genera avisos ni rompe', () => {
    expect(tipos('compras', {})).toEqual([])
    expect(tipos('proveedor', {})).toEqual([])
    expect(tipos('capataz', {})).toEqual([])
  })
})

describe('agruparPorOrden', () => {
  it('una orden con un solo aviso va sola y sin título; los avisos sin orden también', () => {
    const datos: DatosAvisos = {
      requisicionesAprobadas: [aprobada('r1')],
      licitaciones: [],
      ordenesCompra: [orden(ORDEN_A, { mensajesNoLeidos: 1 })],
    }
    const renglones = agruparPorOrden(avisosDeFlujo('compras', datos, {}, AHORA))
    expect(renglones).toHaveLength(2)
    expect(renglones.every((r) => r.titulo === undefined)).toBe(true)
  })
})
