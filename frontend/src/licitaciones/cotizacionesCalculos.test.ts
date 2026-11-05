import { describe, expect, it } from 'vitest'
import {
  calcularPrecioTotalCotizacion,
  formatoPrecio,
} from './cotizacionesCalculos'

describe('cotizacionesCalculos', () => {
  const licitacion = {
    licitacionId: 'lic-1',
    requisicionId: 'req-1',
    obraId: 'obra-1',
    estado: 'Abierta',
    fechaCreacion: '2026-10-01T00:00:00Z',
    fechaLimiteCotizacion: '2026-10-20T00:00:00Z',
    items: [
      {
        articuloId: 'art-cemento',
        descripcion: 'Cemento 50kg',
        cantidad: 80,
        unidadMedida: 'BOLSA',
      },
      {
        articuloId: 'art-arena',
        descripcion: 'Arena fina',
        cantidad: 15,
        unidadMedida: 'M3',
      },
    ],
  }

  it('calcula el precio total como la suma de precioUnitario × cantidad de cada ítem de la licitación', () => {
    const cotizacion = {
      items: [
        { articuloId: 'art-cemento', precioUnitario: 1000 },
        { articuloId: 'art-arena', precioUnitario: 2000 },
      ],
    }

    // 80 * 1000 + 15 * 2000 = 80000 + 30000 = 110000
    const total = calcularPrecioTotalCotizacion(cotizacion, licitacion)
    expect(total).toBe(110000)
  })

  it('asume cantidad 0 si un ítem cotizado no existe en el pliego de la licitación', () => {
    const cotizacion = {
      items: [
        { articuloId: 'art-desconocido', precioUnitario: 5000 },
        { articuloId: 'art-cemento', precioUnitario: 1000 },
      ],
    }

    const total = calcularPrecioTotalCotizacion(cotizacion, licitacion)
    expect(total).toBe(80000)
  })

  it('devuelve 0 si la cotización no tiene ítems', () => {
    const cotizacion = { items: [] }
    const total = calcularPrecioTotalCotizacion(cotizacion, licitacion)
    expect(total).toBe(0)
  })

  it('formatea el precio con signo $ y separadores locales sin código de moneda ARS', () => {
    expect(formatoPrecio(850000)).toMatch(/^\$\s*850\.000$/)
    expect(formatoPrecio(0)).toMatch(/^\$\s*0$/)
  })
})
