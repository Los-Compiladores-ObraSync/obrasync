import { describe, expect, it } from 'vitest'
import type { CotizacionResponseDto } from './licitacionesTipos'
import {
  calcularPrecioTotalCotizacion,
  calcularTotalConFlete,
  desglosarCotizacion,
  estrellasDePuntaje,
  formatoPrecio,
  marcasDeComparacion,
  ordenarPorCalificacion,
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

  describe('detalle para Compras', () => {
    const barata = {
      cotizacionId: 'c-1',
      flete: 15000,
      plazoEntregaDias: 5,
      items: [
        { articuloId: 'art-cemento', precioUnitario: 8000 },
        { articuloId: 'art-arena', precioUnitario: 14000 },
      ],
    }
    const cara = {
      cotizacionId: 'c-2',
      flete: 25000,
      plazoEntregaDias: 10,
      items: [
        { articuloId: 'art-cemento', precioUnitario: 8500 },
        { articuloId: 'art-arena', precioUnitario: 16000 },
      ],
    }

    it('el total con flete suma el flete al precio total (y sin flete suma 0)', () => {
      expect(calcularTotalConFlete(barata, licitacion)).toBe(850000 + 15000)
      expect(calcularTotalConFlete({ items: barata.items }, licitacion)).toBe(850000)
    })

    it('el desglose trae una línea por material del pliego con su subtotal', () => {
      const lineas = desglosarCotizacion(barata, licitacion)

      expect(lineas).toEqual([
        {
          articuloId: 'art-cemento',
          descripcion: 'Cemento 50kg',
          cantidad: 80,
          unidadMedida: 'BOLSA',
          precioUnitario: 8000,
          subtotal: 640000,
        },
        {
          articuloId: 'art-arena',
          descripcion: 'Arena fina',
          cantidad: 15,
          unidadMedida: 'M3',
          precioUnitario: 14000,
          subtotal: 210000,
        },
      ])
    })

    it('un material que el proveedor no cotizó queda sin precio y con subtotal 0', () => {
      const lineas = desglosarCotizacion({ items: [barata.items[0]] }, licitacion)

      expect(lineas[1].precioUnitario).toBeUndefined()
      expect(lineas[1].subtotal).toBe(0)
    })

    it('marca la mejor oferta en precio, flete y plazo', () => {
      const marcas = marcasDeComparacion([barata, cara], licitacion)

      expect(marcas.get('c-1')).toEqual(['Más barata', 'Flete más bajo', 'Entrega más rápida'])
      expect(marcas.get('c-2')).toEqual([])
    })

    it('reparte las marcas cuando cada oferta gana en algo distinto', () => {
      const rapida = { ...cara, plazoEntregaDias: 2 }
      const marcas = marcasDeComparacion([barata, rapida], licitacion)

      expect(marcas.get('c-1')).toEqual(['Más barata', 'Flete más bajo'])
      expect(marcas.get('c-2')).toEqual(['Entrega más rápida'])
    })

    it('"Más barata" cuenta el flete: la de precio menor puede perder si su flete es mucho más caro', () => {
      const materialesBaratosFleteCaro = { ...barata, flete: 200000 }   // 850.000 + 200.000 = 1.050.000
      const marcas = marcasDeComparacion([materialesBaratosFleteCaro, cara], licitacion) // cara: 920.000 + 25.000

      expect(marcas.get('c-1')).not.toContain('Más barata')
      expect(marcas.get('c-2')).toContain('Más barata')
    })

    it('con una sola cotización no marca nada', () => {
      expect(marcasDeComparacion([barata], licitacion).get('c-1')).toEqual([])
    })

    it('si hay empate en un criterio no marca ese criterio, y si el empate es en lo mejor, marca a todas', () => {
      const igualFlete = { ...cara, flete: 15000 }
      const empatadas = marcasDeComparacion([barata, igualFlete], licitacion)
      expect(empatadas.get('c-1')).not.toContain('Flete más bajo')
      expect(empatadas.get('c-2')).not.toContain('Flete más bajo')

      const tres = marcasDeComparacion(
        [barata, { ...barata, cotizacionId: 'c-3' }, cara],
        licitacion,
      )
      expect(tres.get('c-1')).toContain('Más barata')
      expect(tres.get('c-3')).toContain('Más barata')
      expect(tres.get('c-2')).toEqual([])
    })

    it('un flete o un plazo sin informar cuentan como 0 e inmediato', () => {
      const sinDatos = { cotizacionId: 'c-4', items: barata.items }
      const marcas = marcasDeComparacion([sinDatos, cara], licitacion)

      expect(marcas.get('c-4')).toEqual(['Más barata', 'Flete más bajo', 'Entrega más rápida'])
    })
  })
})

describe('ordenarPorCalificacion', () => {
  function oferta(
    id: string,
    puntajeTotal: number,
    extra: Partial<CotizacionResponseDto> = {},
  ): CotizacionResponseDto {
    return {
      cotizacionId: id,
      licitacionId: 'lic-1',
      proveedorId: `prov-${id}`,
      flete: 0,
      plazoEntregaDias: 5,
      fechaCreacion: '2026-11-01T10:00:00Z',
      items: [],
      puntajeTotal,
      esGanadora: false,
      ...extra,
    } as CotizacionResponseDto
  }

  it('deja arriba la de mayor calificación', () => {
    const lista = [oferta('a', 40), oferta('b', 90), oferta('c', 65)]

    expect(ordenarPorCalificacion(lista).map((o) => o.cotizacionId)).toEqual(['b', 'c', 'a'])
  })

  it('con la misma calificación gana la que entrega más rápido, y después la que se cotizó antes', () => {
    const lista = [
      oferta('lenta', 80, { plazoEntregaDias: 10 }),
      oferta('tarde', 80, { plazoEntregaDias: 3, fechaCreacion: '2026-11-05T10:00:00Z' }),
      oferta('temprano', 80, { plazoEntregaDias: 3, fechaCreacion: '2026-11-02T10:00:00Z' }),
    ]

    expect(ordenarPorCalificacion(lista).map((o) => o.cotizacionId)).toEqual([
      'temprano',
      'tarde',
      'lenta',
    ])
  })

  it('la oferta destacada va siempre primera', () => {
    const lista = [oferta('a', 70), oferta('b', 70, { esGanadora: true })]

    expect(ordenarPorCalificacion(lista)[0].cotizacionId).toBe('b')
  })

  it('no modifica la lista original', () => {
    const lista = [oferta('a', 10), oferta('b', 20)]

    ordenarPorCalificacion(lista)

    expect(lista.map((o) => o.cotizacionId)).toEqual(['a', 'b'])
  })
})

describe('estrellasDePuntaje', () => {
  it('cada estrella son 20 puntos, redondeando', () => {
    expect(estrellasDePuntaje(100)).toBe(5)
    expect(estrellasDePuntaje(97.5)).toBe(5)
    expect(estrellasDePuntaje(70)).toBe(4) // 3,5 redondea hacia arriba
    expect(estrellasDePuntaje(50)).toBe(3)
    expect(estrellasDePuntaje(29)).toBe(1)
    expect(estrellasDePuntaje(9)).toBe(0)
    expect(estrellasDePuntaje(0)).toBe(0)
  })

  it('nunca se sale de 0 a 5', () => {
    expect(estrellasDePuntaje(130)).toBe(5)
    expect(estrellasDePuntaje(-20)).toBe(0)
    expect(estrellasDePuntaje(Number.NaN)).toBe(0)
  })
})
