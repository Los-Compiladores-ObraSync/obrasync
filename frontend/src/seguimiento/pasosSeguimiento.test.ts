import { describe, expect, it } from 'vitest'
import type { RequisicionResponseDto } from '../requisiciones/requisicionesTipos'
import { armarPasos } from './pasosSeguimiento'
import type { SeguimientoRequisicion } from './seguimientoService'

function req(estado: RequisicionResponseDto['estado'], extra: Partial<RequisicionResponseDto> = {}): RequisicionResponseDto {
  return {
    requisicionId: 'r1',
    numeroRequisicion: 'REQ-1',
    obraId: 'o1',
    solicitanteId: 'u1',
    estado,
    fechaCreacion: '2026-11-02T12:00:00Z',
    items: [],
    ...extra,
  }
}

const sinLicitacion: SeguimientoRequisicion = { requisicionId: 'r1', cantidadCotizaciones: 0, ordenesCompra: [] }
const orden = { ordenCompraId: 'oc1', estado: 'Emitida', fechaEmision: '2026-11-05T12:00:00Z', proveedorId: 'p1', proveedorNombre: 'Corralón Sur' }

describe('armarPasos', () => {
  it('pendiente: se queda esperando a Compras', () => {
    const pasos = armarPasos(req('Pendiente'), null, [])
    expect(pasos.map((p) => p.clave)).toEqual(['enviado', 'compras'])
    expect(pasos[1]).toMatchObject({ estado: 'actual', titulo: 'Esperando que Compras lo apruebe' })
  })

  it('rechazada: muestra el motivo como problema y no sigue', () => {
    const pasos = armarPasos(req('Rechazada', { motivoRechazo: 'Falta justificar' }), null, [])
    expect(pasos.at(-1)).toMatchObject({ estado: 'problema', detalle: 'Falta justificar' })
    expect(pasos).toHaveLength(2)
  })

  it('cancelada: termina ahí', () => {
    const pasos = armarPasos(req('Cancelada'), null, [])
    expect(pasos.at(-1)).toMatchObject({ clave: 'compras', estado: 'problema' })
  })

  it('aprobada sin licitación: Compras todavía no pidió cotizaciones', () => {
    const pasos = armarPasos(req('Aprobada'), sinLicitacion, [])
    expect(pasos.find((p) => p.clave === 'licitacion')).toMatchObject({ clave: 'licitacion', estado: 'actual', titulo: 'Compras todavía no pidió cotizaciones' })
  })

  it('licitación abierta: cuenta las ofertas', () => {
    const pasos = armarPasos(
      req('Aprobada'),
      { ...sinLicitacion, licitacionId: 'l1', estadoLicitacion: 'Abierta', cantidadCotizaciones: 2, fechaLimiteCotizacion: '2026-11-20T12:00:00Z' },
      [],
    )
    expect(pasos.find((p) => p.clave === 'licitacion')?.detalle).toContain('2 ofertas recibidas')
  })

  it('orden emitida sin despachar: espera el despacho', () => {
    const pasos = armarPasos(
      req('Aprobada'),
      { ...sinLicitacion, licitacionId: 'l1', estadoLicitacion: 'Adjudicada', cantidadCotizaciones: 1, ordenesCompra: [orden] },
      [],
    )
    expect(pasos.find((p) => p.clave === 'orden')).toMatchObject({ estado: 'hecho', detalle: 'Corralón Sur' })
    expect(pasos.find((p) => p.clave === 'despacho')).toMatchObject({ estado: 'actual' })
    expect(pasos.find((p) => p.clave === 'recepcion')).toMatchObject({ estado: 'pendiente' })
  })

  it('despachada y con recepción parcial, y después completa', () => {
    const seg = {
      ...sinLicitacion,
      licitacionId: 'l1',
      estadoLicitacion: 'Adjudicada',
      cantidadCotizaciones: 1,
      ordenesCompra: [{ ...orden, estado: 'Despachada', fechaDespacho: '2026-11-06T12:00:00Z' }],
    }
    const parcial = armarPasos(req('Aprobada'), seg, [{ estado: 2 }])
    expect(parcial.find((p) => p.clave === 'despacho')?.estado).toBe('hecho')
    expect(parcial.find((p) => p.clave === 'recepcion')).toMatchObject({ estado: 'actual', titulo: 'Recibiste una parte' })

    const completa = armarPasos(req('Aprobada'), seg, [{ estado: 2 }, { estado: 3 }])
    expect(completa.find((p) => p.clave === 'recepcion')).toMatchObject({ estado: 'hecho', titulo: 'Recibido en obra' })
  })
})
